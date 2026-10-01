using AterraEngine.Core.DependencyInjection.Tests.Fixtures;

namespace AterraEngine.Core.DependencyInjection.Tests.Scopes;
public sealed class LifecycleAndScopeApiTests {
    [Test]
    public async Task SynchronousDisposalUsesSyncInterfaceAndReverseOrder() {
        var log = new List<string>();
        ServiceProvider provider = new ServiceCollection()
            .AddFactory<SyncResource>(ServiceLifetime.Host, factory: _ => new SyncResource(log, "host"))
            .AddFactory<SyncResourceChild>(ServiceLifetime.Of<AterraWorld>(), factory: _ => new SyncResourceChild(log, "world"))
            .Build();
        OwnedServiceScope world = provider.CreateScope<AterraWorld>();

        _ = await provider.ResolveAsync<SyncResource>();
        _ = await world.ResolveAsync<SyncResourceChild>();

        provider.Dispose();
        provider.Dispose();

        await Assert.That(string.Join(",", log)).IsEqualTo("world,host");
    }

    [Test]
    public async Task SynchronousDisposalReportsAsyncOnlyResourcesAndContinuesCleanup() {
        var sync = new SyncResource([], "sync");
        var asyncOnly = new AsyncOnlyResource();
        ServiceProvider provider = new ServiceCollection()
            .AddInstance(sync, ServiceInstanceOwnership.Container)
            .AddInstance(asyncOnly, ServiceInstanceOwnership.Container)
            .Build();

        var error = Check.Fails<AggregateException>(action: () => provider.Dispose(), "async-only");

        await Assert.That(sync.Count).IsEqualTo(1);
        await Assert.That(asyncOnly.Count).IsEqualTo(0);
        Check.Same(error, Check.Fails<AggregateException>(() => provider.Dispose()));
    }

    [Test]
    public async Task FirstDisposalModeWinsAndConcurrentCallsShareCompletion() {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new DualResource(entered, release);
        ServiceProvider provider = new ServiceCollection()
            .AddInstance(resource, ServiceInstanceOwnership.Container)
            .Build();

        ValueTask asyncDisposal = provider.DisposeAsync();
        await entered.Task;
        Task synchronousDisposal = Task.Run(() => provider.Dispose());
        Check.True(!synchronousDisposal.IsCompleted, "Synchronous disposal did not await the first async disposal.");
        release.SetResult();

        await asyncDisposal;
        await synchronousDisposal;
        await Assert.That(resource.AsyncCount).IsEqualTo(1);
        await Assert.That(resource.SyncCount).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeScopeApiMatchesGenericApiAndValidatesRelationshipsAndInputs() {
        ServiceProvider provider = new ServiceCollection()
            .DeclareScope<CustomScope>(typeof(AterraWorld))
            .RequireInput<CustomScope, ScopeInput>()
            .Build();
        OwnedServiceScope world = provider.CreateScope(typeof(AterraWorld));
        var input = new ScopeInput();
        OwnedServiceScope custom = world.CreateScope(typeof(CustomScope), ServiceScopeInput.Of(input));

        await Assert.That(custom.ScopeType).IsEqualTo(typeof(CustomScope));
        OwnedServiceScope genericCustom = world.CreateScope<CustomScope>(ServiceScopeInput.Of(new ScopeInput()));
        await Assert.That(genericCustom.ScopeType).IsEqualTo(custom.ScopeType);
        // ReSharper disable once AccessToDisposedClosure
        await Assert.That(() => provider.CreateScope(typeof(AterraScene)))
            .ThrowsExactly<DependencyInjectionException>();
        await Assert.That(() => world.CreateScope(typeof(CustomScope)))
            .ThrowsExactly<DependencyInjectionException>().WithMessageContaining("Required input");
        // ReSharper disable once AccessToDisposedClosure
        await Assert.That(() => provider.CreateScope(null!)).ThrowsExactly<ArgumentNullException>();

        provider.Dispose();
    }

    [Test]
    public async Task EmptyScopeDisposalLeavesParentUsable() {
        await using ServiceProvider provider = new ServiceCollection().Build();
        OwnedServiceScope world = provider.CreateScope<AterraWorld>();

        await world.DisposeAsync();

        await Assert.That(await provider.ResolveAsync<ServiceProvider>()).IsSameReferenceAs(provider);
    }

    [Test]
    public async Task ProjectOwnedScopeContractsPreserveRuntimeScopeAndAsyncAlias() {
        await using ServiceProvider provider = new ServiceCollection().Build();
        IServiceScopeFactory factory = provider;

        IServiceScope scope = factory.CreateScope(typeof(AterraWorld));
        await Assert.That(scope).IsTypeOf<OwnedServiceScope>();
        await Assert.That(scope.ServiceProvider).IsTypeOf<ServiceProvider>();
        await Assert.That(scope.ServiceProvider).IsSameReferenceAs(provider);
        await scope.DisposeAsync();

        OwnedServiceScope asyncScope = provider.CreateAsyncScope(typeof(AterraWorld));
        await Assert.That(asyncScope.Parent).IsSameReferenceAs(provider.Host);
        await asyncScope.DisposeAsync();
    }

    [Test]
    public async Task ParameterlessCompatibilityFactoryUsesCanonicalWorldScope() {
        await using ServiceProvider provider = new ServiceCollection().Build();
        IServiceScope scope = provider.CreateScope();

        await Assert.That(((OwnedServiceScope)scope).ScopeType).IsEqualTo(typeof(AterraWorld));
        await scope.DisposeAsync();
    }

    [Test]
    public async Task ScopedCacheAndOwnedCleanupRemainOrderedAfterLazyInitialization() {
        var disposed = new List<string>();
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<ScopedResource>(ServiceLifetime.Of<AterraWorld>(), factory: _ => new ScopedResource(disposed))
            .Build();
        OwnedServiceScope world = provider.CreateScope<AterraWorld>();

        var first = await world.ResolveAsync<ScopedResource>();
        var second = await world.ResolveAsync<ScopedResource>();
        await world.DisposeAsync();

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(disposed).IsEquivalentTo(["resource"]);
    }

    private sealed class CustomScope;

    private sealed class ScopeInput;

    private class SyncResource(List<string> log, string name) : IDisposable {
        public int Count { get; private set; }
        public void Dispose() {
            Count++;
            log.Add(name);
        }
    }

    private sealed class SyncResourceChild(List<string> log, string name) : SyncResource(log, name);

    private sealed class AsyncOnlyResource : IAsyncDisposable {
        public int Count { get; private set; }
        public ValueTask DisposeAsync() {
            Count++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DualResource(TaskCompletionSource entered, TaskCompletionSource release) : IDisposable, IAsyncDisposable {
        public int AsyncCount { get; private set; }
        public int SyncCount { get; private set; }
        public async ValueTask DisposeAsync() {
            AsyncCount++;
            entered.SetResult();
            await release.Task;
        }
        public void Dispose() => SyncCount++;
    }

    private sealed class ScopedResource(List<string> disposed) : IDisposable {
        public void Dispose() => disposed.Add("resource");
    }
}
