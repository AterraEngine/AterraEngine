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
public class CollectionBenchmarks {
    private readonly ServiceProvider[] _aterra = new ServiceProvider[3];
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider[] _microsoft = new Microsoft.Extensions.DependencyInjection.ServiceProvider[3];
    [GlobalSetup]
    public void Setup() {
        for (int index = 0; index < 3; index++) {
            int count = index switch { 0 => 1, 1 => 4, _ => 16 };
            _aterra[index] = BenchmarkFactories.CreateAterraCollectionServices(count).Build();
            _microsoft[index] = BenchmarkFactories.CreateMicrosoftCollectionServices(count).BuildServiceProvider(BenchmarkFactories.ProviderOptions);
        }
    }
    [GlobalCleanup]
    public void Cleanup() {
        foreach (ServiceProvider provider in _aterra) provider.DisposeAsync().GetAwaiter().GetResult();
        foreach (Microsoft.Extensions.DependencyInjection.ServiceProvider provider in _microsoft) provider.Dispose();
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve IEnumerable (1)")]
    public ICollectionItem[] MicrosoftCollection1() => (ICollectionItem[])_microsoft[0].GetServices<ICollectionItem>();
    [Benchmark]
    [BenchmarkCategory("Resolve IEnumerable (1)")]
    public ICollectionItem[] AterraCollection1() => (ICollectionItem[])BenchmarkFactories.Resolve<IEnumerable<ICollectionItem>>(_aterra[0]);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve IEnumerable (4)")]
    public ICollectionItem[] MicrosoftCollection4() => (ICollectionItem[])_microsoft[1].GetServices<ICollectionItem>();
    [Benchmark]
    [BenchmarkCategory("Resolve IEnumerable (4)")]
    public ICollectionItem[] AterraCollection4() => (ICollectionItem[])BenchmarkFactories.Resolve<IEnumerable<ICollectionItem>>(_aterra[1]);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve IEnumerable (16)")]
    public ICollectionItem[] MicrosoftCollection16() => (ICollectionItem[])_microsoft[2].GetServices<ICollectionItem>();
    [Benchmark]
    [BenchmarkCategory("Resolve IEnumerable (16)")]
    public ICollectionItem[] AterraCollection16() => (ICollectionItem[])BenchmarkFactories.Resolve<IEnumerable<ICollectionItem>>(_aterra[2]);
}
