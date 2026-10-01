// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;

namespace AterraEngine.Core.DependencyInjection.Benchmarks;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class CoreResolutionBenchmarks {
    private ServiceProvider _aterra = null!;
    private OwnedServiceScope _aterraWorld = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _microsoft = null!;
    private Microsoft.Extensions.DependencyInjection.IServiceScope _microsoftScope = null!;
    [GlobalSetup]
    public void Setup() {
        _aterra = BenchmarkFactories.CreateAterraServices().Build();
        _aterraWorld = _aterra.CreateScope<AterraWorld>();
        _microsoft = BenchmarkFactories.CreateMicrosoftServices().BuildServiceProvider(BenchmarkFactories.ProviderOptions);
        _microsoftScope = _microsoft.CreateScope();
        _ = BenchmarkFactories.Resolve<SingletonService>(_aterra);
        _ = BenchmarkFactories.Resolve<ScopedService>(_aterraWorld);
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
    public SingletonService AterraResolveSingleton() => BenchmarkFactories.Resolve<SingletonService>(_aterra);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve transient")]
    public TransientService MicrosoftResolveTransient() => _microsoft.GetRequiredService<TransientService>();
    [Benchmark]
    [BenchmarkCategory("Resolve transient")]
    public TransientService AterraResolveTransient() => BenchmarkFactories.Resolve<TransientService>(_aterra);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve scoped (cached)")]
    public ScopedService MicrosoftResolveScoped() => _microsoftScope.ServiceProvider.GetRequiredService<ScopedService>();
    [Benchmark]
    [BenchmarkCategory("Resolve scoped (cached)")]
    public ScopedService AterraResolveScoped() => BenchmarkFactories.Resolve<ScopedService>(_aterraWorld);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve 8-level transient graph")]
    public Chain1 MicrosoftResolveDeepGraph() => _microsoft.GetRequiredService<Chain1>();
    [Benchmark]
    [BenchmarkCategory("Resolve 8-level transient graph")]
    public Chain1 AterraResolveDeepGraph() => BenchmarkFactories.Resolve<Chain1>(_aterra);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve mixed graph")]
    public RequestHandler MicrosoftResolveMixedGraph() => _microsoftScope.ServiceProvider.GetRequiredService<RequestHandler>();
    [Benchmark]
    [BenchmarkCategory("Resolve mixed graph")]
    public RequestHandler AterraResolveMixedGraph() => BenchmarkFactories.Resolve<RequestHandler>(_aterraWorld);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve generated closed generic closure")]
    public IClosedBenchmark<string> MicrosoftClosedGeneric() => _microsoft.GetRequiredService<IClosedBenchmark<string>>();
    [Benchmark]
    [BenchmarkCategory("Resolve generated closed generic closure")]
    public IClosedBenchmark<string> AterraClosedGeneric() => BenchmarkFactories.Resolve<IClosedBenchmark<string>>(_aterra);
}
