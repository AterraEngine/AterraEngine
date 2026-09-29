using System.Runtime.CompilerServices;
using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection.Tests;
public class OwnershipTests {
    [Test]
    public async Task ChildrenAndDependentsDisposeFirstExactlyOnce() {
        // Arrange
        var log = new List<string>();
        ServiceProvider host = new ServiceCollection()
            .AddFactory<First>(Lifetime.Host, factory: _ => new First(log, "host"))
            .AddFactory<Second>(Lifetime.Of<World>(), factory: r => {
                r.Get<First>();
                return new Second(log, "world");
            })
            .AddFactory<Third>(Lifetime.Transient, factory: _ => new Third(log, "transient"))
            .AddFactory<Fourth>(Lifetime.Of<Scene>(), factory: r => {
                r.Get<Second>();
                r.Get<Third>();
                return new Fourth(log, "scene");
            }).Build();
        OwnedScope scene = host.CreateScope<World>().CreateScope<Scene>();

        // Act
        var instance = await scene.ResolveAsync<Fourth>();
        await host.DisposeAsync();
        await host.DisposeAsync();
        await scene.DisposeAsync();

        // Assert
        await Assert.That(string.Join(",", log)).IsEqualTo("scene,transient,world,host");
        await Assert.That(instance.Count).IsEqualTo(1);
        await Check.FailsAsync<ObjectDisposedException>(() => scene.ResolveAsync<Fourth>().AsTask());
        Check.Fails<ObjectDisposedException>(() => host.CreateScope<World>());
    }

    [Test]
    public async Task ExternalOwnershipIsExplicitEvenIfNeverResolved() {
        // Arrange
        var caller = new First([], "caller");
        var owned = new Second([], "owned");
        ServiceProvider host = new ServiceCollection().AddInstance(caller, InstanceOwnership.Caller)
            .AddInstance(owned, InstanceOwnership.Container).Build();

        // Act
        Check.Same(caller, await host.ResolveAsync<First>());
        await host.DisposeAsync();

        // Assert
        await Assert.That(caller.Count).IsEqualTo(0);
        await Assert.That(owned.Count).IsEqualTo(1);
        caller.Dispose();
    }

    [Test]
    public async Task FailureCleansUnpublishedTransientsButKeepsCachedDependencies() {
        // Arrange
        var log = new List<string>();
        ServiceProvider host = new ServiceCollection()
            .AddFactory<First>(Lifetime.Host, factory: _ => new First(log, "cached"))
            .AddFactory<Second>(Lifetime.Transient, factory: _ => new Second(log, "temporary"))
            .AddFactory<Failure>(Lifetime.Host, factory: r => {
                r.Get<First>();
                r.Get<Second>();
                throw new InvalidOperationException("broken");
            }).Build();
        await using ServiceProvider cleanup = host;

        // Act
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<Failure>().AsTask(), "broken");

        // Assert
        await Assert.That(string.Join(",", log)).IsEqualTo("temporary");
        var cached = await host.ResolveAsync<First>();
        await Assert.That(cached.Count).IsEqualTo(0);
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<Failure>().AsTask(), "broken");
        await host.DisposeAsync();
        await Assert.That(string.Join(",", log)).IsEqualTo("temporary,cached");
    }

    [Test]
    public async Task NestedActivationFailureReversesActualConstructionOrder() {
        // Arrange
        var log = new List<string>();
        await using ServiceProvider host = new ServiceCollection()
            .AddFactory<First>(Lifetime.Transient, factory: _ => new First(log, "first"))
            .AddFactory<Second>(Lifetime.Transient, factory: _ => new Second(log, "second"))
            .AddFactory<Failure>(Lifetime.Transient, factory: r => {
                r.Get<Second>();
                throw new InvalidOperationException("inner");
            })
            .AddFactory<OuterFailure>(Lifetime.Host, factory: r => {
                r.Get<First>();
                r.Get<Failure>();
                return new OuterFailure();
            }).Build();

        // Act
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<OuterFailure>().AsTask(), "inner");

        // Assert
        await Assert.That(string.Join(",", log)).IsEqualTo("second,first");
    }

    [Test]
    public async Task AsyncOnlyFailureCleanupIsAwaitedByResolutionAndShutdown() {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new AsyncOnlyResource(entered, release);
        ServiceProvider host = new ServiceCollection().AddFactory<AsyncOnlyResource>(Lifetime.Transient, factory: _ => resource)
            .AddFactory<Failure>(Lifetime.Host, factory: r => {
                r.Get<AsyncOnlyResource>();
                throw new InvalidOperationException("activation");
            }).Build();
        await using ServiceProvider cleanup = host;

        // Act
        Task resolution = host.ResolveAsync<Failure>().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check.True(!resolution.IsCompleted, "Resolution did not await rollback.");
        Task shutdown = host.DisposeAsync().AsTask();
        Check.True(!shutdown.IsCompleted, "Shutdown did not await in-flight rollback.");
        release.SetResult();
        await Check.FailsAsync<DependencyInjectionException>(action: () => resolution, "activation");
        await shutdown;

        // Assert
        await Assert.That(resource.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DisposalFailuresAreAggregatedAndDoNotStopCleanup() {
        // Arrange
        var log = new List<string>();
        ServiceProvider host = new ServiceCollection()
            .AddFactory<First>(Lifetime.Host, factory: _ => new First(log, "host", true))
            .AddFactory<Second>(Lifetime.Of<World>(), factory: _ => new Second(log, "world", true)).Build();
        await host.ResolveAsync<First>();
        OwnedScope world = host.CreateScope<World>();
        await world.ResolveAsync<Second>();

        // Act
        var error = await Check.FailsAsync<AggregateException>(() => host.DisposeAsync().AsTask());

        // Assert
        await Assert.That(error.Flatten().InnerExceptions.Count).IsEqualTo(2);
        Check.Same(error, await Check.FailsAsync<AggregateException>(() => host.DisposeAsync().AsTask()));
        await Assert.That(string.Join(",", log)).IsEqualTo("world,host");
    }

    [Test]
    public async Task ActivationAndRollbackFailuresAreBothReported() {
        // Arrange
        var log = new List<string>();
        await using ServiceProvider host = new ServiceCollection()
            .AddFactory<First>(Lifetime.Transient, factory: _ => new First(log, "first"))
            .AddFactory<Second>(Lifetime.Transient, factory: _ => new Second(log, "second", true))
            .AddFactory<Failure>(Lifetime.Transient, factory: r => {
                r.Get<First>();
                r.Get<Second>();
                throw new InvalidOperationException("activation-error");
            }).Build();

        // Act
        var error = await Check.FailsAsync<AggregateException>(action: () => host.ResolveAsync<Failure>().AsTask(), "activation-error");

        // Assert
        Check.True(error.ToString().Contains("dispose-second"), "Rollback error was lost.");
        await Assert.That(string.Join(",", log)).IsEqualTo("second,first");
    }

    [Test]
    public async Task WorldTeardownIsIndependentAndTransientOwnersAreAnchored() {
        // Arrange
        var log = new List<string>();
        await using ServiceProvider host = new ServiceCollection()
            .AddFactory<First>(Lifetime.Transient, factory: _ => new First(log, "helper"))
            .AddFactory<Second>(Lifetime.Of<World>(), factory: r => {
                r.Get<First>();
                return new Second(log, "world");
            }).Build();
        OwnedScope a = host.CreateScope<World>();
        OwnedScope scene = a.CreateScope<Scene>();
        OwnedScope b = host.CreateScope<World>();

        // Act
        await scene.ResolveAsync<Second>();
        var live = await b.ResolveAsync<Second>();
        await scene.DisposeAsync();

        // Assert
        Check.True(log.Count == 0, "World resources were owned by the requesting scene.");
        await a.DisposeAsync();
        await Assert.That(string.Join(",", log)).IsEqualTo("world,helper");
        Check.Same(live, await b.ResolveAsync<Second>());
        await Assert.That(live.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FactoryAliasesCannotDoubleOwnExternalObjectsOrInputs() {
        // Arrange
        var input = new First([], "input");
        ServiceProvider host = new ServiceCollection().RequireInput<World, First>()
            .AddFactory<IDisposable>(Lifetime.Of<World>(), factory: r => r.Get<First>()).Build();
        await using ServiceProvider cleanup = host;
        OwnedScope world = host.CreateScope<World>(ScopeInput.Of(input));

        // Act
        await Check.FailsAsync<DependencyInjectionException>(action: () => world.ResolveAsync<IDisposable>().AsTask(), "already owned");
        await host.DisposeAsync();

        // Assert
        await Assert.That(input.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RepeatedScopeCyclesReleaseTrackedResourcesAndInputs() {
        // Arrange
        await using ServiceProvider host = new ServiceCollection().RequireInput<World, ContainerTests.WorldConfig>()
            .AddFactory<First>(Lifetime.Of<World>(), factory: _ => new First([], "world"))
            .AddFactory<Second>(Lifetime.Transient, factory: _ => new Second([], "scene")).Build();
        var references = new List<WeakReference>();

        // Act
        for (int index = 0; index < 30; index++) {
            references.AddRange(await CreateAndDispose(host));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert
        Check.True(references.All(reference => !reference.IsAlive), "Disposed scope resources remain retained by the host.");
        GC.KeepAlive(host);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> CreateAndDispose(ServiceProvider host) {
        var input = new ContainerTests.WorldConfig(1);
        OwnedScope world = host.CreateScope<World>(ScopeInput.Of(input));
        OwnedScope scene = world.CreateScope<Scene>();
        var first = await scene.ResolveAsync<First>();
        var second = await scene.ResolveAsync<Second>();
        await world.DisposeAsync();
        return [new WeakReference(world), new WeakReference(scene), new WeakReference(first), new WeakReference(second), new WeakReference(input)];
    }

    [Test]
    public async Task GeneratedConstructorFailureRollsBackButBuildNeverConstructs() {
        // Arrange
        var log = new List<string>();
        var services = new ServiceCollection();
        OwnershipActivators.AddActivators(services);
        services.AddFactory<First>(Lifetime.Transient, factory: _ => new First(log, "temporary"))
            .AddFactory<Second>(Lifetime.Host, factory: _ => new Second(log, "cached"))
            .Add<ThrowingConstructor>(Lifetime.Host);
        ServiceProvider host = services.Build();
        await using ServiceProvider cleanup = host;

        // Act
        Func<Task> resolve = () => host.ResolveAsync<ThrowingConstructor>().AsTask();

        // Assert
        Check.True(log.Count == 0, "Build instantiated a user service.");
        await Check.FailsAsync<DependencyInjectionException>(resolve, "constructor-error");
        await Assert.That(string.Join(",", log)).IsEqualTo("temporary");
        var cached = await host.ResolveAsync<Second>();
        await Assert.That(cached.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CachedDependencyKeepsItsOwnTransientAfterParentFailure() {
        // Arrange
        var log = new List<string>();
        ServiceProvider host = new ServiceCollection()
            .AddFactory<First>(Lifetime.Transient, factory: _ => new First(log, "cached-helper"))
            .AddFactory<Second>(Lifetime.Host, factory: r => {
                r.Get<First>();
                return new Second(log, "cached");
            })
            .AddFactory<Third>(Lifetime.Transient, factory: _ => new Third(log, "temporary"))
            .AddFactory<Failure>(Lifetime.Host, factory: r => {
                r.Get<Third>();
                r.Get<Second>();
                throw new InvalidOperationException("failed");
            }).Build();
        await using ServiceProvider cleanup = host;

        // Act
        await Check.FailsAsync<DependencyInjectionException>(() => host.ResolveAsync<Failure>().AsTask());

        // Assert
        await Assert.That(string.Join(",", log)).IsEqualTo("temporary");
        await host.DisposeAsync();
        await Assert.That(string.Join(",", log)).IsEqualTo("temporary,cached,cached-helper");
    }

    [Test]
    public async Task AlreadyOwnedAndExternalFactoryAliasesAreRejectedWithoutDoubleDisposal() {
        // Arrange
        var external = new First([], "external");
        ServiceProvider host = new ServiceCollection().AddInstance(external, InstanceOwnership.Caller)
            .AddFactory<IDisposable>(Lifetime.Transient, factory: r => r.Get<First>()).Build();
        await using ServiceProvider cleanup = host;

        // Act
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<IDisposable>().AsTask(), "already owned");
        await host.DisposeAsync();

        // Assert
        await Assert.That(external.Count).IsEqualTo(0);
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().AddInstance(external, InstanceOwnership.Caller)
            .AddInstance<IDisposable>(external, InstanceOwnership.Container).Build(), "same external object");

        var owned = new First([], "owned");
        ServiceProvider second = new ServiceCollection().AddFactory<First>(Lifetime.Host, factory: _ => owned)
            .AddFactory<IDisposable>(Lifetime.Transient, factory: r => r.Get<First>()).Build();
        await using ServiceProvider secondCleanup = second;
        await Check.FailsAsync<DependencyInjectionException>(action: () => second.ResolveAsync<IDisposable>().AsTask(), "already owned");
        await second.DisposeAsync();
        await Assert.That(owned.Count).IsEqualTo(1);
    }

    public sealed class ThrowingConstructor {
        public ThrowingConstructor(First transient, Second cached) {
            _ = transient;
            _ = cached;
            throw new InvalidOperationException("constructor-error");
        }
    }

    public class Resource(List<string> log, string name, bool fail = false) : IDisposable {
        public int Count { get; private set; }
        public void Dispose() {
            Count++;
            log.Add(name);
            if (fail) throw new InvalidOperationException($"dispose-{name}");
        }
    }

    public sealed class First(List<string> log, string name, bool fail = false) : Resource(log, name, fail);

    public sealed class Second(List<string> log, string name, bool fail = false) : Resource(log, name, fail);

    public sealed class Third(List<string> log, string name) : Resource(log, name);

    public sealed class Fourth(List<string> log, string name) : Resource(log, name);

    public sealed class Failure;

    public sealed class OuterFailure;

    public sealed class AsyncOnlyResource(TaskCompletionSource entered, TaskCompletionSource release) : IAsyncDisposable {
        public int Count { get; private set; }
        public async ValueTask DisposeAsync() {
            Count++;
            entered.TrySetResult();
            await release.Task;
        }
    }

    public sealed class AsyncResource(TaskCompletionSource entered, TaskCompletionSource release) : IDisposable, IAsyncDisposable {
        public int AsyncCount { get; private set; }
        public int SyncCount { get; private set; }
        public async ValueTask DisposeAsync() {
            AsyncCount++;
            entered.TrySetResult();
            await release.Task;
        }
        public void Dispose() => SyncCount++;
    }
}

[GenerateServiceActivators(typeof(OwnershipTests.ThrowingConstructor))]
internal static partial class OwnershipActivators;
