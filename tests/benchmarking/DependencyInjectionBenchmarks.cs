// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
// ReSharper disable once RedundantUsingDirective
using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using AterraProvider = AterraEngine.Core.DependencyInjection.ServiceProvider;
using AterraServices = AterraEngine.Core.DependencyInjection.Collection.ServiceCollection;
using MicrosoftProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

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
    private OwnedScope _aterraWorld = null!;
    private MicrosoftProvider _microsoft = null!;
    private IServiceScope _microsoftScope = null!;

    [GlobalSetup]
    public void Setup() {
        _aterra = CreateAterraServices().Build();
        _aterraWorld = _aterra.CreateScope<World>();
        _microsoft = CreateMicrosoftServices().BuildServiceProvider(ProviderOptions);
        _microsoftScope = _microsoft.CreateScope();

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
        using IServiceScope scope = _microsoft.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ScopedService>();
    }

    [Benchmark]
    [BenchmarkCategory("Create, resolve and dispose scope")]
    public ScopedService AterraScopeLifecycle() {
        OwnedScope scope = _aterra.CreateScope<World>();
        ScopedService service = Resolve<ScopedService>(scope);
        scope.DisposeAsync().GetAwaiter().GetResult();
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

    private static ServiceProviderOptions ProviderOptions => new() {
        ValidateOnBuild = true,
        ValidateScopes = true
    };

    private static AterraServices CreateAterraServices() {
        var services = new AterraServices();
        BenchmarkActivators.AddActivators(services);
        return services
            .Add<SingletonService>(Lifetime.Host)
            .Add<TransientService>(Lifetime.Transient)
            .Add<ScopedService>(Lifetime.Of<World>())
            .Add<Chain1>(Lifetime.Transient)
            .Add<Chain2>(Lifetime.Transient)
            .Add<Chain3>(Lifetime.Transient)
            .Add<Chain4>(Lifetime.Transient)
            .Add<Chain5>(Lifetime.Transient)
            .Add<Chain6>(Lifetime.Transient)
            .Add<Chain7>(Lifetime.Transient)
            .Add<Chain8>(Lifetime.Transient)
            .Add<GraphLeaf>(Lifetime.Transient)
            .Add<LeftBranch>(Lifetime.Transient)
            .Add<RightBranch>(Lifetime.Transient)
            .Add<RequestHandler>(Lifetime.Transient);
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
        .AddTransient<RequestHandler>();

    private static T Resolve<T>(AterraProvider provider) where T : notnull
        => provider.ResolveAsync<T>().GetAwaiter().GetResult();

    private static T Resolve<T>(OwnedScope scope) where T : notnull
        => scope.ResolveAsync<T>().GetAwaiter().GetResult();
}

[GenerateServiceActivators(
    typeof(SingletonService), typeof(TransientService), typeof(ScopedService),
    typeof(Chain1), typeof(Chain2), typeof(Chain3), typeof(Chain4),
    typeof(Chain5), typeof(Chain6), typeof(Chain7), typeof(Chain8),
    typeof(GraphLeaf), typeof(LeftBranch), typeof(RightBranch), typeof(RequestHandler)
)]
internal static partial class BenchmarkActivators;

public sealed class SingletonService;
public sealed class TransientService;
public sealed class ScopedService;

public sealed class Chain1(Chain2 next) { public Chain2 Next { get; } = next; }
public sealed class Chain2(Chain3 next) { public Chain3 Next { get; } = next; }
public sealed class Chain3(Chain4 next) { public Chain4 Next { get; } = next; }
public sealed class Chain4(Chain5 next) { public Chain5 Next { get; } = next; }
public sealed class Chain5(Chain6 next) { public Chain6 Next { get; } = next; }
public sealed class Chain6(Chain7 next) { public Chain7 Next { get; } = next; }
public sealed class Chain7(Chain8 next) { public Chain8 Next { get; } = next; }
public sealed class Chain8(TransientService leaf) { public TransientService Leaf { get; } = leaf; }

public sealed class GraphLeaf;
public sealed class LeftBranch(GraphLeaf leaf) { public GraphLeaf Leaf { get; } = leaf; }
public sealed class RightBranch(GraphLeaf leaf) { public GraphLeaf Leaf { get; } = leaf; }
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
