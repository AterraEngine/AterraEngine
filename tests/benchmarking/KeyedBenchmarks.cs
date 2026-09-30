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
public class KeyedBenchmarks {
    private ServiceProvider _aterra = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _microsoft = null!;
    [GlobalSetup]
    public void Setup() {
        _aterra = BenchmarkFactories.CreateAterraKeyedServices().Build();
        _microsoft = BenchmarkFactories.CreateMicrosoftKeyedServices().BuildServiceProvider(BenchmarkFactories.ProviderOptions);
        _ = BenchmarkFactories.ResolveKeyed<KeyedSingleton>(_aterra);
        _ = _microsoft.GetKeyedService<KeyedSingleton>(BenchmarkFactories.KeyedKey);
    }
    [GlobalCleanup]
    public void Cleanup() {
        _aterra.DisposeAsync().GetAwaiter().GetResult();
        _microsoft.Dispose();
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve keyed singleton (cached)")]
    public KeyedSingleton MicrosoftKeyedCached() => _microsoft.GetKeyedService<KeyedSingleton>(BenchmarkFactories.KeyedKey)!;
    [Benchmark]
    [BenchmarkCategory("Resolve keyed singleton (cached)")]
    public KeyedSingleton AterraKeyedCached() => BenchmarkFactories.ResolveKeyed<KeyedSingleton>(_aterra);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve keyed transient")]
    public KeyedTransient MicrosoftKeyedTransient() => _microsoft.GetKeyedService<KeyedTransient>(BenchmarkFactories.KeyedKey)!;
    [Benchmark]
    [BenchmarkCategory("Resolve keyed transient")]
    public KeyedTransient AterraKeyedTransient() => BenchmarkFactories.ResolveKeyed<KeyedTransient>(_aterra);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve keyed collection")]
    public IKeyedItem[] MicrosoftKeyedCollection() => (IKeyedItem[])_microsoft.GetKeyedServices<IKeyedItem>(BenchmarkFactories.KeyedKey);
    [Benchmark]
    [BenchmarkCategory("Resolve keyed collection")]
    public IKeyedItem[] AterraKeyedCollection() => _aterra.ResolveKeyedEnumerableAsync<IKeyedItem, string>(BenchmarkFactories.KeyedKey).GetAwaiter().GetResult();
}
