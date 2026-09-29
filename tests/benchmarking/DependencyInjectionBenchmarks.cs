// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
// ReSharper disable once RedundantUsingDirective
using AterraEngine.Core.DependencyInjection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using AterraProvider=AterraEngine.Core.DependencyInjection.ServiceProvider;
using AterraServices=AterraEngine.Core.DependencyInjection.ServiceCollection;
using MicrosoftProvider=Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace AterraEngine.Core.DependencyInjection.Benchmarks;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class DependencyInjectionBenchmarks {
    private AterraProvider _aterra = null!;
    private OwnedServiceScope _aterraWorld = null!;
    private MicrosoftProvider _microsoft = null!;
    private Microsoft.Extensions.DependencyInjection.IServiceScope _microsoftScope = null!;
    private readonly AterraProvider[] _collectionProviders = new AterraProvider[3];
    private readonly MicrosoftProvider[] _microsoftCollectionProviders = new MicrosoftProvider[3];
    private AterraProvider _aterraKeyed = null!;
    private MicrosoftProvider _microsoftKeyed = null!;
    private AterraProvider _aterraDecorated = null!;
    private AterraProvider _aterraTripleDecorated = null!;
    private MicrosoftProvider _microsoftDecorated = null!;
    private MicrosoftProvider _microsoftTripleDecorated = null!;
    private AterraProvider _aterraDiagnosticsOff = null!;
    private AterraProvider _aterraDiagnosticsNoOp = null!;
    private AterraProvider _aterraDiagnosticsAllocation = null!;
    private MicrosoftProvider _microsoftDisposal = null!;
    private AterraProvider _aterraDisposal = null!;

    [GlobalSetup]
    public void Setup() {
        _aterra = CreateAterraServices().Build();
        _aterraWorld = _aterra.CreateScope<AterraWorld>();
        _microsoft = CreateMicrosoftServices().BuildServiceProvider(ProviderOptions);
        _microsoftScope = _microsoft.CreateScope();

        for (int index = 0; index < 3; index++) {
            int count = index switch { 0 => 1, 1 => 4, _ => 16 };
            _collectionProviders[index] = CreateAterraCollectionServices(count).Build();
            _microsoftCollectionProviders[index] = CreateMicrosoftCollectionServices(count).BuildServiceProvider(ProviderOptions);
        }

        _aterraKeyed = CreateAterraKeyedServices().Build();
        _microsoftKeyed = CreateMicrosoftKeyedServices().BuildServiceProvider(ProviderOptions);
        _aterraDecorated = CreateAterraDecoratedServices(1).Build();
        _aterraTripleDecorated = CreateAterraDecoratedServices(3).Build();
        _microsoftDecorated = CreateMicrosoftDecoratedServices(1).BuildServiceProvider(ProviderOptions);
        _microsoftTripleDecorated = CreateMicrosoftDecoratedServices(3).BuildServiceProvider(ProviderOptions);
        _aterraDisposal = CreateAterraDisposalServices().Build();
        _microsoftDisposal = CreateMicrosoftDisposalServices().BuildServiceProvider(ProviderOptions);
        _aterraDiagnosticsOff = CreateAterraDiagnosticsServices(null).Build();
        _aterraDiagnosticsNoOp = CreateAterraDiagnosticsServices(new ServiceDiagnosticsOptions(NoOpDiagnosticSink.Instance)).Build();
        _aterraDiagnosticsAllocation = CreateAterraDiagnosticsServices(new ServiceDiagnosticsOptions(NoOpDiagnosticSink.Instance, true)).Build();

        // Ensure cached-resolution benchmarks do not include first activation.
        _ = Resolve<SingletonService>(_aterra);
        _ = Resolve<ScopedService>(_aterraWorld);
        _ = _microsoft.GetRequiredService<SingletonService>();
        _ = _microsoftScope.ServiceProvider.GetRequiredService<ScopedService>();
    }

    [GlobalCleanup]
    public void Cleanup() {
        _aterraWorld.DisposeAsync().GetAwaiter().GetResult();
        _aterra.DisposeAsync().GetAwaiter().GetResult();
        _microsoftScope.Dispose();
        _microsoft.Dispose();
        foreach (AterraProvider provider in _collectionProviders) provider.DisposeAsync().GetAwaiter().GetResult();
        foreach (MicrosoftProvider provider in _microsoftCollectionProviders) provider.Dispose();
        _aterraKeyed.DisposeAsync().GetAwaiter().GetResult();
        _microsoftKeyed.Dispose();
        _aterraDecorated.DisposeAsync().GetAwaiter().GetResult();
        _aterraTripleDecorated.DisposeAsync().GetAwaiter().GetResult();
        _microsoftDecorated.Dispose();
        _microsoftTripleDecorated.Dispose();
        _aterraDisposal.DisposeAsync().GetAwaiter().GetResult();
        _microsoftDisposal.Dispose();
        _aterraDiagnosticsOff.DisposeAsync().GetAwaiter().GetResult();
        _aterraDiagnosticsNoOp.DisposeAsync().GetAwaiter().GetResult();
        _aterraDiagnosticsAllocation.DisposeAsync().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve singleton (cached)")]
    public SingletonService MicrosoftResolveSingleton() => _microsoft.GetRequiredService<SingletonService>();

    [Benchmark]
    [BenchmarkCategory("Resolve singleton (cached)")]
    public SingletonService AterraResolveSingleton() => Resolve<SingletonService>(_aterra);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve transient")]
    public TransientService MicrosoftResolveTransient() => _microsoft.GetRequiredService<TransientService>();

    [Benchmark]
    [BenchmarkCategory("Resolve transient")]
    public TransientService AterraResolveTransient() => Resolve<TransientService>(_aterra);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve scoped (cached)")]
    public ScopedService MicrosoftResolveScoped() => _microsoftScope.ServiceProvider.GetRequiredService<ScopedService>();

    [Benchmark]
    [BenchmarkCategory("Resolve scoped (cached)")]
    public ScopedService AterraResolveScoped() => Resolve<ScopedService>(_aterraWorld);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve 8-level transient graph")]
    public Chain1 MicrosoftResolveDeepGraph() => _microsoft.GetRequiredService<Chain1>();

    [Benchmark]
    [BenchmarkCategory("Resolve 8-level transient graph")]
    public Chain1 AterraResolveDeepGraph() => Resolve<Chain1>(_aterra);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve mixed graph")]
    public RequestHandler MicrosoftResolveMixedGraph() => _microsoftScope.ServiceProvider.GetRequiredService<RequestHandler>();

    [Benchmark]
    [BenchmarkCategory("Resolve mixed graph")]
    public RequestHandler AterraResolveMixedGraph() => Resolve<RequestHandler>(_aterraWorld);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Create, resolve and dispose scope")]
    public ScopedService MicrosoftScopeLifecycle() {
        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _microsoft.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ScopedService>();
    }

    [Benchmark]
    [BenchmarkCategory("Create, resolve and dispose scope")]
    public ScopedService AterraScopeLifecycle() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        var service = Resolve<ScopedService>(scope);
        scope.DisposeAsync().GetAwaiter().GetResult();
        return service;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Empty scope create/dispose")]
    public void MicrosoftEmptyScopeCreateDispose() {
        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _microsoft.CreateScope();
    }

    [Benchmark]
    [BenchmarkCategory("Empty scope create/dispose")]
    public void AterraEmptyScopeCreateDispose() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        scope.Dispose();
    }

    [Benchmark]
    [BenchmarkCategory("Scoped cache creation")]
    public ScopedService AterraScopedCacheCreation() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        var service = Resolve<ScopedService>(scope);
        scope.Dispose();
        return service;
    }

    [Benchmark]
    [BenchmarkCategory("Disposable-resource cleanup")]
    public DisposableTransient AterraDisposableResourceCleanup() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        var service = Resolve<DisposableTransient>(scope);
        scope.Dispose();
        return service;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Register, build and dispose")]
    public void MicrosoftBuildLifecycle() {
        using MicrosoftProvider provider = CreateMicrosoftServices().BuildServiceProvider(ProviderOptions);
    }

    [Benchmark]
    [BenchmarkCategory("Register, build and dispose")]
    public void AterraBuildLifecycle() {
        AterraProvider provider = CreateAterraServices().Build();
        provider.DisposeAsync().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve IEnumerable (1)")]
    public ICollectionItem[] MicrosoftCollection1() => (ICollectionItem[])_microsoftCollectionProviders[0].GetServices<ICollectionItem>();
    [Benchmark]
    [BenchmarkCategory("Resolve IEnumerable (1)")]
    public ICollectionItem[] AterraCollection1() => (ICollectionItem[])Resolve<IEnumerable<ICollectionItem>>(_collectionProviders[0]);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve IEnumerable (4)")]
    public ICollectionItem[] MicrosoftCollection4() => (ICollectionItem[])_microsoftCollectionProviders[1].GetServices<ICollectionItem>();
    [Benchmark]
    [BenchmarkCategory("Resolve IEnumerable (4)")]
    public ICollectionItem[] AterraCollection4() => (ICollectionItem[])Resolve<IEnumerable<ICollectionItem>>(_collectionProviders[1]);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve IEnumerable (16)")]
    public ICollectionItem[] MicrosoftCollection16() => (ICollectionItem[])_microsoftCollectionProviders[2].GetServices<ICollectionItem>();
    [Benchmark]
    [BenchmarkCategory("Resolve IEnumerable (16)")]
    public ICollectionItem[] AterraCollection16() => (ICollectionItem[])Resolve<IEnumerable<ICollectionItem>>(_collectionProviders[2]);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve keyed singleton (cached)")]
    public KeyedSingleton MicrosoftKeyedCached() => _microsoftKeyed.GetKeyedService<KeyedSingleton>(KeyedKey)!;
    [Benchmark]
    [BenchmarkCategory("Resolve keyed singleton (cached)")]
    public KeyedSingleton AterraKeyedCached() => ResolveKeyed<KeyedSingleton>(_aterraKeyed);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve keyed transient")]
    public KeyedTransient MicrosoftKeyedTransient() => _microsoftKeyed.GetKeyedService<KeyedTransient>(KeyedKey)!;
    [Benchmark]
    [BenchmarkCategory("Resolve keyed transient")]
    public KeyedTransient AterraKeyedTransient() => ResolveKeyed<KeyedTransient>(_aterraKeyed);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve keyed collection")]
    public IKeyedItem[] MicrosoftKeyedCollection() => (IKeyedItem[])_microsoftKeyed.GetKeyedServices<IKeyedItem>(KeyedKey);
    [Benchmark]
    [BenchmarkCategory("Resolve keyed collection")]
    public KeyedCollectionItem[] AterraKeyedCollection() => _aterraKeyed.ResolveKeyedEnumerableAsync<KeyedCollectionItem, string>(KeyedKey).GetAwaiter().GetResult();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve one decorator")]
    public IDecoratedService MicrosoftOneDecorator() => _microsoftDecorated.GetRequiredService<IDecoratedService>();
    [Benchmark]
    [BenchmarkCategory("Resolve one decorator")]
    public IDecoratedService AterraOneDecorator() => Resolve<IDecoratedService>(_aterraDecorated);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve three decorators")]
    public IDecoratedService MicrosoftThreeDecorators() => _microsoftTripleDecorated.GetRequiredService<IDecoratedService>();
    [Benchmark]
    [BenchmarkCategory("Resolve three decorators")]
    public IDecoratedService AterraThreeDecorators() => Resolve<IDecoratedService>(_aterraTripleDecorated);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve generated closed generic closure")]
    public IClosedBenchmark<string> MicrosoftClosedGeneric() => _microsoft.GetRequiredService<IClosedBenchmark<string>>();
    [Benchmark]
    [BenchmarkCategory("Resolve generated closed generic closure")]
    public IClosedBenchmark<string> AterraClosedGeneric() => Resolve<IClosedBenchmark<string>>(_aterra);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Dispose sync resource")]
    public void MicrosoftSyncDisposal() {
        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _microsoftDisposal.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<SyncDisposable>();
    }
    [Benchmark]
    [BenchmarkCategory("Dispose sync resource")]
    public void AterraSyncDisposal() {
        OwnedServiceScope scope = _aterraDisposal.CreateScope<AterraWorld>();
        _ = Resolve<SyncDisposable>(scope);
        scope.Dispose();
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Dispose async resource")]
    public void MicrosoftAsyncDisposal() {
        AsyncServiceScope scope = _microsoftDisposal.CreateAsyncScope();
        _ = scope.ServiceProvider.GetRequiredService<AsyncDisposable>();
        scope.DisposeAsync().GetAwaiter().GetResult();
    }
    [Benchmark]
    [BenchmarkCategory("Dispose async resource")]
    public void AterraAsyncDisposal() {
        OwnedServiceScope scope = _aterraDisposal.CreateScope<AterraWorld>();
        _ = Resolve<AsyncDisposable>(scope);
        scope.DisposeAsync().GetAwaiter().GetResult();
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Dispose dual-interface resource")]
    public void MicrosoftDualDisposal() {
        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _microsoftDisposal.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<DualDisposable>();
    }
    [Benchmark]
    [BenchmarkCategory("Dispose dual-interface resource")]
    public void AterraDualDisposal() {
        OwnedServiceScope scope = _aterraDisposal.CreateScope<AterraWorld>();
        _ = Resolve<DualDisposable>(scope);
        scope.DisposeAsync().GetAwaiter().GetResult();
    }

    [Benchmark]
    [BenchmarkCategory("Diagnostics disabled")]
    public DiagnosticService DiagnosticsDisabled() => Resolve<DiagnosticService>(_aterraDiagnosticsOff);
    [Benchmark]
    [BenchmarkCategory("Diagnostics no-op sink")]
    public DiagnosticService DiagnosticsNoOp() => Resolve<DiagnosticService>(_aterraDiagnosticsNoOp);
    [Benchmark]
    [BenchmarkCategory("Diagnostics allocation measurement")]
    public DiagnosticService DiagnosticsAllocationMeasurement() => Resolve<DiagnosticService>(_aterraDiagnosticsAllocation);

    private static ServiceProviderOptions ProviderOptions => new() {
        ValidateOnBuild = true,
        ValidateScopes = true
    };

    private static AterraServices CreateAterraServices() {
        return new AterraServices().RegisterActivators<DependencyInjectionBenchmarks>()
            .AddGeneratedCollectionResolver<string>(static (ref resolver) => resolver.GetAll<string>());
    }

    private static IServiceCollection CreateMicrosoftServices() => new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        .AddSingleton<SingletonService>()
        .AddTransient<TransientService>()
        .AddScoped<ScopedService>()
        .AddTransient<Chain1>()
        .AddTransient<Chain2>()
        .AddTransient<Chain3>()
        .AddTransient<Chain4>()
        .AddTransient<Chain5>()
        .AddTransient<Chain6>()
        .AddTransient<Chain7>()
        .AddTransient<Chain8>()
        .AddTransient<GraphLeaf>()
        .AddTransient<LeftBranch>()
        .AddTransient<RightBranch>()
        .AddTransient<RequestHandler>()
        .AddSingleton<IClosedBenchmark<string>, ClosedBenchmark<string>>();

    private static AterraServices CreateAterraCollectionServices(int count) {
        AterraServices services = CreateAterraServices();
        for (int index = 0; index < count; index++) {
            services.AddEnumerable<ICollectionItem, CollectionItem>(ServiceLifetime.Transient);
        }

        return services;
    }

    private static IServiceCollection CreateMicrosoftCollectionServices(int count) {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        for (int index = 0; index < count; index++) {
            services.AddTransient<ICollectionItem, CollectionItem>();
        }

        return services;
    }

    private static AterraServices CreateAterraKeyedServices() => CreateAterraServices()
        .AddKeyed<KeyedSingleton, KeyedSingleton, string>(ServiceLifetime.Singleton, KeyedKey)
        .AddKeyed<KeyedTransient, KeyedTransient, string>(ServiceLifetime.Transient, KeyedKey)
        .AddKeyedEnumerable<KeyedCollectionItem, KeyedCollectionItem, string>(ServiceLifetime.Transient, KeyedKey);

    private static IServiceCollection CreateMicrosoftKeyedServices() => new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        .AddKeyedSingleton<KeyedSingleton>(KeyedKey)
        .AddKeyedTransient<KeyedTransient>(KeyedKey)
        .AddKeyedTransient<IKeyedItem, KeyedCollectionItem>(KeyedKey);

    private static AterraServices CreateAterraDecoratedServices(int count) {
        ServiceCollection services = CreateAterraServices().Add<IDecoratedService, DecoratedService>(ServiceLifetime.Transient)
            .Decorate<IDecoratedService, DecoratorOne>();
        if (count == 3) services.Decorate<IDecoratedService, DecoratorTwo>().Decorate<IDecoratedService, DecoratorThree>();
        return services;
    }

    private static IServiceCollection CreateMicrosoftDecoratedServices(int count) {
        IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddTransient<DecoratedService>();
        if (count == 1)
            services.AddTransient<IDecoratedService>(static provider =>
                new DecoratorOne(provider.GetRequiredService<DecoratedService>()));
        else
            services.AddTransient<IDecoratedService>(static provider =>
                new DecoratorThree(new DecoratorTwo(new DecoratorOne(provider.GetRequiredService<DecoratedService>()))));
        return services;
    }

    private static AterraServices CreateAterraDisposalServices() => CreateAterraServices()
        .Add<SyncDisposable>(ServiceLifetime.Transient).Add<AsyncDisposable>(ServiceLifetime.Transient).Add<DualDisposable>(ServiceLifetime.Transient);
    private static IServiceCollection CreateMicrosoftDisposalServices() => new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        .AddTransient<SyncDisposable>().AddTransient<AsyncDisposable>().AddTransient<DualDisposable>();

    private static AterraServices CreateAterraDiagnosticsServices(ServiceDiagnosticsOptions? options) {
        ServiceCollection services = CreateAterraServices().Add<DiagnosticService>(ServiceLifetime.Transient);
        return options is null ? services : services.ConfigureDiagnostics(options);
    }

    private static T Resolve<T>(AterraProvider provider) where T : notnull
        => provider.ResolveAsync<T>().GetAwaiter().GetResult();

    private static T Resolve<T>(OwnedServiceScope scope) where T : notnull
        => scope.ResolveAsync<T>().GetAwaiter().GetResult();
    private static T ResolveKeyed<T>(AterraProvider provider) where T : notnull
        => provider.ResolveKeyedAsync<T, string>(KeyedKey).GetAwaiter().GetResult();
    private const string KeyedKey = "benchmark";
}

[SingletonService<SingletonService>]
public sealed class SingletonService;

[TransientService<TransientService>]
public sealed class TransientService;

[WorldService<ScopedService>]
public sealed class ScopedService;

[WorldService<DisposableTransient>]
public sealed class DisposableTransient : IDisposable {
    public void Dispose() {}
}

[TransientService<Chain1>]
public sealed class Chain1(Chain2 next) {
    public Chain2 Next { get; } = next;
}

[TransientService<Chain2>]
public sealed class Chain2(Chain3 next) {
    public Chain3 Next { get; } = next;
}

[TransientService<Chain3>]
public sealed class Chain3(Chain4 next) {
    public Chain4 Next { get; } = next;
}

[TransientService<Chain4>]
public sealed class Chain4(Chain5 next) {
    public Chain5 Next { get; } = next;
}

[TransientService<Chain5>]
public sealed class Chain5(Chain6 next) {
    public Chain6 Next { get; } = next;
}

[TransientService<Chain6>]
public sealed class Chain6(Chain7 next) {
    public Chain7 Next { get; } = next;
}

[TransientService<Chain7>]
public sealed class Chain7(Chain8 next) {
    public Chain8 Next { get; } = next;
}

[TransientService<Chain8>]
public sealed class Chain8(TransientService leaf) {
    public TransientService Leaf { get; } = leaf;
}

[TransientService<GraphLeaf>]
public sealed class GraphLeaf;

[TransientService<LeftBranch>]
public sealed class LeftBranch(GraphLeaf leaf) {
    public GraphLeaf Leaf { get; } = leaf;
}

[TransientService<RightBranch>]
public sealed class RightBranch(GraphLeaf leaf) {
    public GraphLeaf Leaf { get; } = leaf;
}

[TransientService<RequestHandler>]
public sealed class RequestHandler(
    SingletonService singleton,
    ScopedService scoped,
    LeftBranch left,
    RightBranch right
) {
    public SingletonService Singleton { get; } = singleton;
    public ScopedService Scoped { get; } = scoped;
    public LeftBranch Left { get; } = left;
    public RightBranch Right { get; } = right;
}

[TransientService<CollectionItem>]
public sealed class CollectionItem : ICollectionItem;

public interface ICollectionItem;

[TransientService<KeyedSingleton>]
public sealed class KeyedSingleton;

[TransientService<KeyedTransient>]
public sealed class KeyedTransient;

[TransientService<KeyedCollectionItem>]
public sealed class KeyedCollectionItem : IKeyedItem;

public interface IKeyedItem;

public interface IDecoratedService;

[TransientService<DecoratedService>]
public sealed class DecoratedService : IDecoratedService;

[TransientService<DecoratorOne>]
public sealed class DecoratorOne([DecoratedDependency<IDecoratedService>] IDecoratedService inner) : IDecoratedService {
    [UsedImplicitly]
    public IDecoratedService Inner { get; } = inner;
}

[TransientService<DecoratorTwo>]
public sealed class DecoratorTwo([DecoratedDependency<IDecoratedService>] IDecoratedService inner) : IDecoratedService {
    [UsedImplicitly]
    public IDecoratedService Inner { get; } = inner;
}

[TransientService<DecoratorThree>]
public sealed class DecoratorThree([DecoratedDependency<IDecoratedService>] IDecoratedService inner) : IDecoratedService {
    [UsedImplicitly]
    public IDecoratedService Inner { get; } = inner;
}

// ReSharper disable once UnusedTypeParameter
public interface IClosedBenchmark<T>;

[GeneratedServiceClosure<IClosedBenchmark<string>, ClosedBenchmark<string>>(ServiceScope.Host)]
public sealed class ClosedBenchmark<T> : IClosedBenchmark<T> where T : class {
    public ClosedBenchmark(IEnumerable<T> values) {}
}

[WorldService<SyncDisposable>]
public sealed class SyncDisposable : IDisposable {
    public void Dispose() {}
}

[WorldService<AsyncDisposable>]
public sealed class AsyncDisposable : IAsyncDisposable {
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[WorldService<DualDisposable>]
public sealed class DualDisposable : IDisposable, IAsyncDisposable {
    public void Dispose() {}
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[TransientService<DiagnosticService>]
public sealed class DiagnosticService;

public sealed class NoOpDiagnosticSink : IServiceDiagnosticSink {
    public static readonly NoOpDiagnosticSink Instance = new();
    private NoOpDiagnosticSink() {}
    public void Write(ServiceDiagnosticEvent diagnosticEvent) {}
}
