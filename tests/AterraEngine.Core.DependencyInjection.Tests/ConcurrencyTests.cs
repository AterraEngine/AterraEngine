using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection.Tests;
public class ConcurrencyTests {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static Task<T> OnThread<T>(Func<Task<T>> action) => Task.Factory.StartNew(action,
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    [Test]
    public async Task ConcurrentCallersShareOneCachedActivation() {
        // Arrange
        using var ready = new Barrier(9);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        await using ServiceProvider host = new ServiceCollection().AddFactory<Service>(Lifetime.Of<World>(), factory: _ => {
            Interlocked.Increment(ref calls);
            entered.Set();
            Check.True(release.Wait(Timeout), "Constructor release timed out.");
            return new Service();
        }).Build();
        OwnedScope world = host.CreateScope<World>();
        OwnedScope[] scenes = Enumerable.Range(0, 8).Select(_ => world.CreateScope<Scene>()).ToArray();

        // Act
        Task<Service>[] resolutions = scenes.Select(scene => OnThread(async () => {
            Check.True(ready.SignalAndWait(Timeout), "Caller barrier timed out.");
            return await scene.ResolveAsync<Service>();
        })).ToArray();
        try {
            Check.True(ready.SignalAndWait(Timeout), "Test barrier timed out.");
            Check.True(entered.Wait(Timeout), "Constructor was not entered.");
            // User code must not hold the provider gate: another world's activation can proceed.
            OwnedScope independent = host.CreateScope<World>();
            independent.CreateScope<Scene>();
        }
        finally { release.Set(); }

        Service[] results = await Task.WhenAll(resolutions).WaitAsync(Timeout);

        // Assert
        Check.True(results.All(result => ReferenceEquals(results[0], result)), "Concurrent callers received different instances.");
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentFailureIsSharedAndPermanentlyFaulted() {
        // Arrange
        using var ready = new Barrier(7);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        await using ServiceProvider host = new ServiceCollection().AddFactory<Service>(Lifetime.Of<World>(), factory: _ => {
            Interlocked.Increment(ref calls);
            entered.Set();
            Check.True(release.Wait(Timeout), "Release timed out.");
            throw new InvalidOperationException("cached-failure");
        }).Build();
        OwnedScope world = host.CreateScope<World>();

        // Act
        Task<DependencyInjectionException>[] resolutions = Enumerable.Range(0, 6).Select(_ => OnThread(async () => {
            Check.True(ready.SignalAndWait(Timeout), "Caller barrier timed out.");
            return await Check.FailsAsync<DependencyInjectionException>(action: () => world.ResolveAsync<Service>().AsTask(), "cached-failure");
        })).ToArray();
        try {
            Check.True(ready.SignalAndWait(Timeout), "Test barrier timed out.");
            Check.True(entered.Wait(Timeout), "Constructor was not entered.");
        }
        finally { release.Set(); }

        DependencyInjectionException[] errors = await Task.WhenAll(resolutions).WaitAsync(Timeout);

        // Assert
        Check.True(errors.All(error => ReferenceEquals(errors[0], error)), "Faulted cache did not share the activation failure.");
        Check.Same(errors[0], await Check.FailsAsync<DependencyInjectionException>(() => world.ResolveAsync<Service>().AsTask()));
        await Assert.That(calls).IsEqualTo(1);
        await world.DisposeAsync();
        await Check.FailsAsync<DependencyInjectionException>(() => host.CreateScope<World>().ResolveAsync<Service>().AsTask());
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task ConcurrentOpaqueFactoryCycleFailsWithoutDeadlock() {
        // Arrange
        using var constructors = new Barrier(2);
        await using ServiceProvider host = new ServiceCollection()
            .AddFactory<Service>(Lifetime.Host, factory: r => {
                Check.True(constructors.SignalAndWait(Timeout), "Factory barrier timed out.");
                r.Get<OtherService>();
                return new Service();
            })
            .AddFactory<OtherService>(Lifetime.Host, factory: r => {
                Check.True(constructors.SignalAndWait(Timeout), "Factory barrier timed out.");
                r.Get<Service>();
                return new OtherService();
            }).Build();

        // Act
        Task<DependencyInjectionException> first = OnThread(() => Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<Service>().AsTask(), "cycle"));
        Task<DependencyInjectionException> second = OnThread(() => Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<OtherService>().AsTask(), "cycle"));

        // Assert
        await Task.WhenAll(first, second).WaitAsync(Timeout);
    }

    [Test]
    public async Task TransientFactoryCyclesAreDetectedAndResolversCannotEscape() {
        // Arrange
        IServiceResolver? escaped = null;
        await using ServiceProvider host = new ServiceCollection()
            .AddFactory<Service>(Lifetime.Transient, factory: r => {
                escaped = r;
                r.Get<OtherService>();
                return new Service();
            })
            .AddFactory<OtherService>(Lifetime.Transient, factory: r => {
                r.Get<Service>();
                return new OtherService();
            }).Build();

        // Act
        Func<Task> resolve = () => host.ResolveAsync<Service>().AsTask();

        // Assert
        await Check.FailsAsync<DependencyInjectionException>(resolve, "cycle");
        Check.Fails<InvalidOperationException>(action: () => escaped!.Get<Service>(), "during its factory");
    }

    [Test]
    public async Task ShutdownWaitsForInFlightConstructionAndRejectsNewWork() {
        // Arrange
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var resource = new Service();
        ServiceProvider host = new ServiceCollection().AddFactory<Service>(Lifetime.Of<World>(), factory: _ => {
            entered.Set();
            Check.True(release.Wait(Timeout), "Release timed out.");
            return resource;
        }).Build();
        OwnedScope world = host.CreateScope<World>();
        OwnedScope scene = world.CreateScope<Scene>();

        // Act
        Task<Service> resolution = OnThread(() => scene.ResolveAsync<Service>().AsTask());
        Task shutdown;
        try {
            Check.True(entered.Wait(Timeout), "Constructor was not entered.");
            shutdown = host.DisposeAsync().AsTask();
            Check.True(!shutdown.IsCompleted, "Shutdown did not wait for construction.");
            Check.True(resource.Count == 0, "Resource was disposed during construction.");
            await Check.FailsAsync<ObjectDisposedException>(() => scene.ResolveAsync<Service>().AsTask());
            Check.Fails<ObjectDisposedException>(() => world.CreateScope<Scene>());
            Check.Fails<ObjectDisposedException>(() => host.CreateScope<World>());
        }
        finally { release.Set(); }

        Check.Same(resource, await resolution.WaitAsync(Timeout));
        await shutdown.WaitAsync(Timeout);

        // Assert
        await Assert.That(resource.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ChildCreationRacingShutdownCannotEscapeParent() {
        // Arrange
        const int iterations = 40;
        int completed = 0;

        // Act
        for (int iteration = 0; iteration < iterations; iteration++) {
            ServiceProvider host = new ServiceCollection().Build();
            using var start = new Barrier(3);
            Task<OwnedScope?> creation = OnThread(() => {
                Check.True(start.SignalAndWait(Timeout), "Creation barrier timed out.");
                try { return Task.FromResult<OwnedScope?>(host.CreateScope<World>()); }
                catch (ObjectDisposedException) { return Task.FromResult<OwnedScope?>(null); }
            });
            Task<bool> disposal = OnThread(async () => {
                Check.True(start.SignalAndWait(Timeout), "Disposal barrier timed out.");
                await host.DisposeAsync();
                return true;
            });
            Check.True(start.SignalAndWait(Timeout), "Test barrier timed out.");
            await Task.WhenAll(creation, disposal).WaitAsync(Timeout);
            if (await creation is {} child) {
                Check.Fails<ObjectDisposedException>(() => child.CreateScope<Scene>());
                await Check.FailsAsync<ObjectDisposedException>(() => child.ResolveAsync<Service>().AsTask());
                await child.DisposeAsync();
            }

            completed++;
        }

        // Assert
        await Assert.That(completed).IsEqualTo(iterations);
    }

    [Test]
    public async Task IndependentWorldCanDisposeWhileOtherWorldConstructs() {
        // Arrange
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        await using ServiceProvider host = new ServiceCollection().AddFactory<Service>(Lifetime.Of<World>(), factory: _ => {
            if (Interlocked.Increment(ref calls) == 2) {
                entered.Set();
                Check.True(release.Wait(Timeout), "Release timed out.");
            }

            return new Service();
        }).Build();
        OwnedScope a = host.CreateScope<World>();
        OwnedScope b = host.CreateScope<World>();
        var first = await a.ResolveAsync<Service>();

        // Act
        Task<Service> second = OnThread(() => b.ResolveAsync<Service>().AsTask());
        try {
            Check.True(entered.Wait(Timeout), "Constructor was not entered.");
            await a.DisposeAsync().AsTask().WaitAsync(Timeout);
            await Assert.That(first.Count).IsEqualTo(1);
        }
        finally { release.Set(); }

        Service live = await second.WaitAsync(Timeout);

        // Assert
        await Assert.That(live.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentShutdownAwaitsOneAsyncCleanup() {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new OwnershipTests.AsyncResource(entered, release);
        ServiceProvider host = new ServiceCollection().AddInstance(resource, InstanceOwnership.Container).Build();

        // Act
        Task first = host.DisposeAsync().AsTask();
        await entered.Task.WaitAsync(Timeout);
        Task second = host.DisposeAsync().AsTask();
        Check.Same(first, second);
        Check.True(!second.IsCompleted, "Repeated shutdown completed before async cleanup.");
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);

        // Assert
        await Assert.That(resource.AsyncCount).IsEqualTo(1);
        await Assert.That(resource.SyncCount).IsEqualTo(0);
    }

    [Test]
    public async Task ReentrantPublicResolutionIsRejectedBeforeItCanDeadlock() {
        // Arrange
        ServiceProvider? provider = null;
        provider = new ServiceCollection().AddFactory<Service>(Lifetime.Host,
            factory: _ => provider!.ResolveAsync<Service>().GetAwaiter().GetResult()).Build();

        // Act
        Func<Task> resolve = () => provider.ResolveAsync<Service>().AsTask();

        // Assert
        await using (provider) {
            await Check.FailsAsync<DependencyInjectionException>(resolve, "Reentrant");
        }
    }

    public sealed class Service : IDisposable {
        public int Count { get; private set; }
        public void Dispose() => Count++;
    }

    public sealed class OtherService;
}
