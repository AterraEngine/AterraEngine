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
public class DiagnosticsBenchmarks {
    private ServiceProvider _allocation = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _microsoft = null!;
    private ServiceProvider _noOp = null!;
    private ServiceProvider _off = null!;
    [GlobalSetup]
    public void Setup() {
        _off = BenchmarkFactories.CreateAterraDiagnosticsServices(null).Build();
        _noOp = BenchmarkFactories.CreateAterraDiagnosticsServices(new ServiceDiagnosticsOptions(NoOpDiagnosticSink.Instance)).Build();
        _allocation = BenchmarkFactories.CreateAterraDiagnosticsServices(new ServiceDiagnosticsOptions(NoOpDiagnosticSink.Instance, true)).Build();
        _microsoft = BenchmarkFactories.CreateMicrosoftServices().BuildServiceProvider(BenchmarkFactories.ProviderOptions);
    }
    [GlobalCleanup]
    public void Cleanup() {
        _off.DisposeAsync().GetAwaiter().GetResult();
        _noOp.DisposeAsync().GetAwaiter().GetResult();
        _allocation.DisposeAsync().GetAwaiter().GetResult();
        _microsoft.Dispose();
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Diagnostics overhead")]
    public DiagnosticService MicrosoftDiagnosticsDisabled() => _microsoft.GetRequiredService<DiagnosticService>();
    [Benchmark]
    [BenchmarkCategory("Diagnostics overhead")]
    public DiagnosticService DiagnosticsDisabled() => BenchmarkFactories.Resolve<DiagnosticService>(_off);
    [Benchmark]
    [BenchmarkCategory("Diagnostics overhead")]
    public DiagnosticService DiagnosticsNoOp() => BenchmarkFactories.Resolve<DiagnosticService>(_noOp);
    [Benchmark]
    [BenchmarkCategory("Diagnostics overhead")]
    public DiagnosticService DiagnosticsAllocationMeasurement() => BenchmarkFactories.Resolve<DiagnosticService>(_allocation);
}
