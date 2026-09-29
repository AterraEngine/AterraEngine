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
public class DecoratorBenchmarks {
    private ServiceProvider _aterra = null!;
    private ServiceProvider _aterraTriple = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _microsoft = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _microsoftTriple = null!;
    [GlobalSetup]
    public void Setup() {
        _aterra = BenchmarkFactories.CreateAterraDecoratedServices(1).Build();
        _aterraTriple = BenchmarkFactories.CreateAterraDecoratedServices(3).Build();
        _microsoft = BenchmarkFactories.CreateMicrosoftDecoratedServices(1).BuildServiceProvider(BenchmarkFactories.ProviderOptions);
        _microsoftTriple = BenchmarkFactories.CreateMicrosoftDecoratedServices(3).BuildServiceProvider(BenchmarkFactories.ProviderOptions);
    }
    [GlobalCleanup]
    public void Cleanup() {
        _aterra.DisposeAsync().GetAwaiter().GetResult();
        _aterraTriple.DisposeAsync().GetAwaiter().GetResult();
        _microsoft.Dispose();
        _microsoftTriple.Dispose();
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve one decorator")]
    public IDecoratedService MicrosoftOneDecorator() => _microsoft.GetRequiredService<IDecoratedService>();
    [Benchmark]
    [BenchmarkCategory("Resolve one decorator")]
    public IDecoratedService AterraOneDecorator() => BenchmarkFactories.Resolve<IDecoratedService>(_aterra);
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Resolve three decorators")]
    public IDecoratedService MicrosoftThreeDecorators() => _microsoftTriple.GetRequiredService<IDecoratedService>();
    [Benchmark]
    [BenchmarkCategory("Resolve three decorators")]
    public IDecoratedService AterraThreeDecorators() => BenchmarkFactories.Resolve<IDecoratedService>(_aterraTriple);
}
