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
public class DisposalBenchmarks {
    private ServiceProvider _aterra = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _microsoft = null!;
    [GlobalSetup]
    public void Setup() {
        _aterra = BenchmarkFactories.CreateAterraDisposalServices().Build();
        _microsoft = BenchmarkFactories.CreateMicrosoftDisposalServices().BuildServiceProvider(BenchmarkFactories.ProviderOptions);
    }
    [GlobalCleanup]
    public void Cleanup() {
        _aterra.DisposeAsync().GetAwaiter().GetResult();
        _microsoft.Dispose();
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Scoped disposable-resource cleanup")]
    public DisposableTransient MicrosoftDisposableResourceCleanup() {
        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _microsoft.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DisposableTransient>();
    }
    [Benchmark]
    [BenchmarkCategory("Scoped disposable-resource cleanup")]
    public DisposableTransient AterraDisposableResourceCleanup() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        try { return BenchmarkFactories.Resolve<DisposableTransient>(scope); }
        finally { scope.Dispose(); }
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Dispose sync resource")]
    public void MicrosoftSyncDisposal() {
        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _microsoft.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<SyncDisposable>();
    }
    [Benchmark]
    [BenchmarkCategory("Dispose sync resource")]
    public void AterraSyncDisposal() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        try { _ = BenchmarkFactories.Resolve<SyncDisposable>(scope); }
        finally { scope.Dispose(); }
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Dispose async resource")]
    public void MicrosoftAsyncDisposal() {
        AsyncServiceScope scope = _microsoft.CreateAsyncScope();
        try { _ = scope.ServiceProvider.GetRequiredService<AsyncDisposable>(); }
        finally { scope.DisposeAsync().GetAwaiter().GetResult(); }
    }
    [Benchmark]
    [BenchmarkCategory("Dispose async resource")]
    public void AterraAsyncDisposal() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        try { _ = BenchmarkFactories.Resolve<AsyncDisposable>(scope); }
        finally { scope.DisposeAsync().GetAwaiter().GetResult(); }
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Dispose dual-interface resource")]
    public void MicrosoftDualDisposal() {
        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _microsoft.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<DualDisposable>();
    }
    [Benchmark]
    [BenchmarkCategory("Dispose dual-interface resource")]
    public void AterraDualDisposal() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        try { _ = BenchmarkFactories.Resolve<DualDisposable>(scope); }
        finally { scope.DisposeAsync().GetAwaiter().GetResult(); }
    }
}
