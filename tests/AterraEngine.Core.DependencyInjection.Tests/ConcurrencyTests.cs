// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection.Tests;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
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
        Barrier readySignal = ready;
        ManualResetEventSlim enteredSignal = entered;
        ManualResetEventSlim releaseSignal = release;
        int calls = 0;
        await using ServiceProvider host = new ServiceCollection().AddFactory<Service>(ServiceLifetime.Of<AterraWorld>(), factory: _ => {
            Interlocked.Increment(ref calls);
            enteredSignal.Set();
            Check.True(releaseSignal.Wait(Timeout), "Constructor release timed out.");
            return new Service();
        }).Build();
        OwnedServiceScope world = host.CreateScope<AterraWorld>();
        OwnedServiceScope[] scenes = Enumerable.Range(0, 8).Select(_ => world.CreateScope<AterraScene>()).ToArray();

        // Act
        Task<Service>[] resolutions = scenes.Select(scene => OnThread(async () => {
            Check.True(readySignal.SignalAndWait(Timeout), "Caller barrier timed out.");
            return await scene.ResolveAsync<Service>();
        })).ToArray();
        try {
            Check.True(ready.SignalAndWait(Timeout), "Test barrier timed out.");
            Check.True(entered.Wait(Timeout), "Constructor was not entered.");
            // User code must not hold the provider gate: another world's activation can proceed.
            OwnedServiceScope independent = host.CreateScope<AterraWorld>();
            independent.CreateScope<AterraScene>();
        }
        finally { release.Set(); }

        Service[] results = await Task.WhenAll(resolutions).WaitAsync(Timeout);

        // Assert
        Check.True(results.All(result => ReferenceEquals(results[0], result)), "Concurrent callers received different instances.");
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task ContendingAsyncResolutionReturnsWithoutBlockingItsCallingThread() {
        // Arrange
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ManualResetEventSlim enteredSignal = entered;
        ManualResetEventSlim releaseSignal = release;
        await using ServiceProvider host = new ServiceCollection().AddFactory<Service>(ServiceLifetime.Host, _ => {
            enteredSignal.Set();
            Check.True(releaseSignal.Wait(Timeout), "Release timed out.");
            return new Service();
        }).Build();
        ServiceProvider resolvingHost = host;
        Task<Service> first = OnThread(() => resolvingHost.ResolveAsync<Service>().AsTask());
        Check.True(entered.Wait(Timeout), "Constructor was not entered.");
        var callReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        Task<Service> second = OnThread(async () => {
            ValueTask<Service> resolution = resolvingHost.ResolveAsync<Service>();
            callReturned.TrySetResult();
            return await resolution;
        });
        try {
            await callReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { release.Set(); }

        // Assert
        Service[] results = await Task.WhenAll(first, second).WaitAsync(Timeout);
        Check.Same(results[0], results[1]);
    }

    [Test]
    public async Task ConcurrentFailureIsSharedAndPermanentlyFaulted() {
        // Arrange
        using var ready = new Barrier(7);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Barrier readySignal = ready;
        ManualResetEventSlim enteredSignal = entered;
        ManualResetEventSlim releaseSignal = release;
        int calls = 0;
        await using ServiceProvider host = new ServiceCollection().AddFactory<Service>(ServiceLifetime.Of<AterraWorld>(), factory: _ => {
            Interlocked.Increment(ref calls);
            enteredSignal.Set();
            Check.True(releaseSignal.Wait(Timeout), "Release timed out.");
            throw new InvalidOperationException("cached-failure");
        }).Build();
        OwnedServiceScope world = host.CreateScope<AterraWorld>();
        ServiceProvider resolvingHost = host;
        OwnedServiceScope resolvingWorld = world;

        // Act
        Task<DependencyInjectionException>[] resolutions = Enumerable.Range(0, 6).Select(_ => OnThread(async () => {
            Check.True(readySignal.SignalAndWait(Timeout), "Caller barrier timed out.");
            return await Check.FailsAsync<DependencyInjectionException>(action: () => resolvingWorld.ResolveAsync<Service>().AsTask(), "cached-failure");
        })).ToArray();
        try {
            Check.True(ready.SignalAndWait(Timeout), "Test barrier timed out.");
            Check.True(entered.Wait(Timeout), "Constructor was not entered.");
        }
        finally { release.Set(); }

        DependencyInjectionException[] errors = await Task.WhenAll(resolutions).WaitAsync(Timeout);

        // Assert
        Check.True(errors.All(error => ReferenceEquals(errors[0], error)), "Faulted cache did not share the activation failure.");
        Check.Same(errors[0], await Check.FailsAsync<DependencyInjectionException>(() => resolvingWorld.ResolveAsync<Service>().AsTask()));
        await Assert.That(calls).IsEqualTo(1);
        await world.DisposeAsync();
        await Check.FailsAsync<DependencyInjectionException>(() => resolvingHost.CreateScope<AterraWorld>().ResolveAsync<Service>().AsTask());
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task CachedFailureIsPublishedAfterAsynchronousRollback() {
        // Arrange
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new OwnershipTests.AsyncOnlyResource(cleanupEntered, releaseCleanup);
        await using ServiceProvider host = new ServiceCollection()
            .AddFactory<OwnershipTests.AsyncOnlyResource>(ServiceLifetime.Transient, _ => resource)
            .AddFactory<Service>(ServiceLifetime.Host, resolver => {
                resolver.Get<OwnershipTests.AsyncOnlyResource>();
                throw new InvalidOperationException("cached-failure");
            }).Build();
        ServiceProvider resolvingHost = host;
        Task<DependencyInjectionException> first = OnThread(() =>
            Check.FailsAsync<DependencyInjectionException>(() => resolvingHost.ResolveAsync<Service>().AsTask(), "cached-failure"));
        await cleanupEntered.Task.WaitAsync(Timeout);

        // Act
        Task<DependencyInjectionException> second = Check.FailsAsync<DependencyInjectionException>(
            () => resolvingHost.ResolveAsync<Service>().AsTask(), "cached-failure");
        Check.True(!second.IsCompleted, "A cache waiter completed before asynchronous rollback.");
        releaseCleanup.SetResult();
        DependencyInjectionException[] errors = await Task.WhenAll(first, second).WaitAsync(Timeout);

        // Assert
        Check.Same(errors[0], errors[1]);
        await Assert.That(resource.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentOpaqueFactoryCycleFailsWithoutDeadlock() {
        // Arrange
        using var constructors = new Barrier(2);
        Barrier constructorSignal = constructors;
        ServiceProvider host = new ServiceCollection()
            .AddFactory<Service>(ServiceLifetime.Host, factory: r => {
                Check.True(constructorSignal.SignalAndWait(Timeout), "Factory barrier timed out.");
                r.Get<OtherService>();
                return new Service();
            })
            .AddFactory<OtherService>(ServiceLifetime.Host, factory: r => {
                Check.True(constructorSignal.SignalAndWait(Timeout), "Factory barrier timed out.");
                r.Get<Service>();
                return new OtherService();
            }).Build();
        await using ServiceProvider cleanup = host;

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
        ServiceProvider host = new ServiceCollection()
            .AddFactory<Service>(ServiceLifetime.Transient, factory: r => {
                escaped = r;
                r.Get<OtherService>();
                return new Service();
            })
            .AddFactory<OtherService>(ServiceLifetime.Transient, factory: r => {
                r.Get<Service>();
                return new OtherService();
            }).Build();
        await using ServiceProvider cleanup = host;

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
        ManualResetEventSlim enteredSignal = entered;
        ManualResetEventSlim releaseSignal = release;
        var resource = new Service();
        ServiceProvider host = new ServiceCollection().AddFactory<Service>(ServiceLifetime.Of<AterraWorld>(), factory: _ => {
            enteredSignal.Set();
            Check.True(releaseSignal.Wait(Timeout), "Release timed out.");
            return resource;
        }).Build();
        OwnedServiceScope world = host.CreateScope<AterraWorld>();
        OwnedServiceScope scene = world.CreateScope<AterraScene>();

        // Act
        Task<Service> resolution = OnThread(() => scene.ResolveAsync<Service>().AsTask());
        Task shutdown;
        try {
            Check.True(entered.Wait(Timeout), "Constructor was not entered.");
            shutdown = host.DisposeAsync().AsTask();
            Check.True(!shutdown.IsCompleted, "Shutdown did not wait for construction.");
            Check.True(resource.Count == 0, "Resource was disposed during construction.");
            await Check.FailsAsync<ObjectDisposedException>(() => scene.ResolveAsync<Service>().AsTask());
            Check.Fails<ObjectDisposedException>(() => world.CreateScope<AterraScene>());
            Check.Fails<ObjectDisposedException>(() => host.CreateScope<AterraWorld>());
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
            Barrier startSignal = start;
            Task<OwnedServiceScope?> creation = OnThread(() => {
                Check.True(startSignal.SignalAndWait(Timeout), "Creation barrier timed out.");
                try { return Task.FromResult<OwnedServiceScope?>(host.CreateScope<AterraWorld>()); }
                catch (ObjectDisposedException) { return Task.FromResult<OwnedServiceScope?>(null); }
            });
            Task<bool> disposal = OnThread(async () => {
                Check.True(startSignal.SignalAndWait(Timeout), "Disposal barrier timed out.");
                await host.DisposeAsync();
                return true;
            });
            Check.True(start.SignalAndWait(Timeout), "Test barrier timed out.");
            await Task.WhenAll(creation, disposal).WaitAsync(Timeout);
            if (await creation is {} child) {
                Check.Fails<ObjectDisposedException>(() => child.CreateScope<AterraScene>());
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
        ManualResetEventSlim enteredSignal = entered;
        ManualResetEventSlim releaseSignal = release;
        int calls = 0;
        await using ServiceProvider host = new ServiceCollection().AddFactory<Service>(ServiceLifetime.Of<AterraWorld>(), factory: _ => {
            if (Interlocked.Increment(ref calls) == 2) {
                enteredSignal.Set();
                Check.True(releaseSignal.Wait(Timeout), "Release timed out.");
            }

            return new Service();
        }).Build();
        OwnedServiceScope a = host.CreateScope<AterraWorld>();
        OwnedServiceScope b = host.CreateScope<AterraWorld>();
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
        ServiceProvider host = new ServiceCollection().AddInstance(resource, ServiceInstanceOwnership.Container).Build();

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
        var holder = new ProviderHolder();
        ServiceProvider provider = new ServiceCollection().AddFactory<Service>(ServiceLifetime.Host,
            factory: _ => holder.Provider!.ResolveAsync<Service>().GetAwaiter().GetResult()).Build();
        holder.Provider = provider;
        ServiceProvider resolvingProvider = provider;

        // Act
        Func<Task> resolve = () => resolvingProvider.ResolveAsync<Service>().AsTask();

        // Assert
        await using (provider) {
            await Check.FailsAsync<DependencyInjectionException>(resolve, "Reentrant");
        }
    }

    [Test]
    public async Task PublicResolutionIntoAnotherProviderIsAllowedDuringActivation() {
        // Arrange
        ServiceProvider dependencyProvider = new ServiceCollection()
            .AddFactory<OtherService>(ServiceLifetime.Host, _ => new OtherService()).Build();
        await using ServiceProvider dependencyCleanup = dependencyProvider;
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<Service>(ServiceLifetime.Host, _ => {
                dependencyProvider.ResolveAsync<OtherService>().GetAwaiter().GetResult();
                return new Service();
            }).Build();

        // Act
        Service service = await provider.ResolveAsync<Service>();

        // Assert
        await Assert.That(service).IsNotNull();
    }

    public sealed class Service : IDisposable {
        public int Count { get; private set; }
        public void Dispose() => Count++;
    }

    public sealed class OtherService;

    private sealed class ProviderHolder {
        internal ServiceProvider? Provider { get; set; }
    }
}
