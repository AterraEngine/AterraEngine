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
public class ScopeLifecycleBenchmarks {
    private ServiceProvider _aterra = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _microsoft = null!;
    [GlobalSetup]
    public void Setup() {
        _aterra = BenchmarkFactories.CreateAterraServices().Build();
        _microsoft = BenchmarkFactories.CreateMicrosoftServices().BuildServiceProvider(BenchmarkFactories.ProviderOptions);
    }
    [GlobalCleanup]
    public void Cleanup() {
        _aterra.DisposeAsync().GetAwaiter().GetResult();
        _microsoft.Dispose();
    }
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
        try { return BenchmarkFactories.Resolve<ScopedService>(scope); }
        finally { scope.Dispose(); }
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Create, resolve and async dispose scope")]
    public ScopedService MicrosoftAsyncScopeLifecycle() {
        AsyncServiceScope scope = _microsoft.CreateAsyncScope();
        try { return scope.ServiceProvider.GetRequiredService<ScopedService>(); }
        finally { scope.DisposeAsync().GetAwaiter().GetResult(); }
    }
    [Benchmark]
    [BenchmarkCategory("Create, resolve and async dispose scope")]
    public ScopedService AterraAsyncScopeLifecycle() {
        OwnedServiceScope scope = _aterra.CreateScope<AterraWorld>();
        try { return BenchmarkFactories.Resolve<ScopedService>(scope); }
        finally { scope.DisposeAsync().GetAwaiter().GetResult(); }
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
        try { return BenchmarkFactories.Resolve<ScopedService>(scope); }
        finally { scope.Dispose(); }
    }
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Scoped cache creation")]
    public ScopedService MicrosoftScopedCacheCreation() {
        using Microsoft.Extensions.DependencyInjection.IServiceScope scope = _microsoft.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ScopedService>();
    }
}
