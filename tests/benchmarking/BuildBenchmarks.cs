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
public class BuildBenchmarks {
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Register, build and dispose")]
    public void MicrosoftBuildLifecycle() {
        using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = BenchmarkFactories.CreateMicrosoftServices().BuildServiceProvider(BenchmarkFactories.ProviderOptions);
    }
    [Benchmark]
    [BenchmarkCategory("Register, build and dispose")]
    public void AterraBuildLifecycle() {
        ServiceProvider provider = BenchmarkFactories.CreateAterraServices().Build();
        provider.DisposeAsync().GetAwaiter().GetResult();
    }
}
