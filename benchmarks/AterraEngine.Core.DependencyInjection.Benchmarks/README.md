# Dependency injection benchmarks

These benchmarks compare AterraEngine's generated-activator path with `Microsoft.Extensions.DependencyInjection` using
equivalent registrations and lifetimes.

The suite measures:

- cached singleton and scoped resolution;
- transient resolution;
- an eight-level transient constructor graph;
- a mixed singleton, scoped, and branching transient graph;
- scope creation, scoped resolution, and disposal;
- registration, validation, provider construction, and disposal.

Run the complete suite from the repository root:

```powershell
dotnet run -c Release --project tests/benchmarking/AterraEngine.Core.DependencyInjection.Benchmarks.csproj
```

Run a category or a short diagnostic job:

```powershell
dotnet run -c Release --project tests/benchmarking/AterraEngine.Core.DependencyInjection.Benchmarks.csproj -- --filter "*Singleton*"
dotnet run -c Release --project tests/benchmarking/AterraEngine.Core.DependencyInjection.Benchmarks.csproj -- --filter "*" --iterationCount 1 --warmupCount 1 --launchCount 1
```

The default benchmark configuration uses three launches, ten warmups, and twenty measured iterations. It intentionally
leaves `InvocationCount` and `UnrollFactor` unset, so BenchmarkDotNet's `DefaultJob` chooses automatic batching. Command
line values override these defaults for smoke runs; the short command above does not run the full repeatable suite.

Microsoft DI is the baseline in each category, so the ratio reports Aterra's time divided by Microsoft DI's time.
MemoryDiagnoser reports managed allocations for both implementations.

Aterra exposes asynchronous resolution and disposal while Microsoft DI resolves synchronously. Resolution benchmarks
synchronously consume Aterra's `ValueTask` so the comparison measures each container's public resolution path without
adding an artificial task wrapper to Microsoft DI. Scope/disposal categories labelled `sync resolve` versus `async dispose`
make that API distinction explicit. Run benchmarks outside Rider's debugger on an otherwise idle machine
and compare results from the same build and hardware.

The keyed collection comparison resolves `IKeyedItem[]` on both containers. Disposal fixtures are registered as scoped on
both sides because the Aterra fixtures use `WorldService` (world-scope) registrations. The aggregate report is written to
`BenchmarkDotNet.Artifacts/results/aggregate-report.md`; it validates that every returned summary has a CSV in one shared
results directory and includes all CSV rows. Median and available GC-generation columns are included when emitted.

## Baseline before resolution-pipeline optimization

The following result is the baseline that motivated the current optimization work. Rerun the complete suite to measure
the current implementation on your machine.

Generated constructor activators now use a stack-only resolver and reuse their cleared resolution context. A focused run
after that change measured 89.63 ns / 24 B for a transient, 620.98 ns / 216 B for the nine-object deep graph, and 406.86
ns / 144 B for the mixed graph. Those byte counts are exactly the resolved service objects, so successful generated
activation adds no managed allocation of its own.

```md
BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 8945HS w/ Radeon 780M Graphics 3.99GHz, 1 CPU, 16 logical and 8 physical cores                                                                                                             
Memory: 15.29 GB Total, 0.44 GB Available                                                                                                                                                              
.NET SDK 11.0.100-rc.1.26425.128                                                                                                                                                                       
[Host]     : .NET 11.0.0 (11.0.0-rc.1.26425.128, 11.0.26.42628), X64 RyuJIT x86-64-v4                                                                                                                
DefaultJob : .NET 11.0.0 (11.0.0-rc.1.26425.128, 11.0.26.42628), X64 RyuJIT x86-64-v4


# Benchmark Aggregate Report

| Method                             | Categories                                   | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio | Gen0   | Gen1   |
|------------------------------------|----------------------------------------------|-----------|-----------|-----------|-----------|-------|---------|-----------|-------------|--------|--------|
| MicrosoftBuildLifecycle            | Register, build and dispose                  | 8.233 μs  | 0.3374 μs | 0.7116 μs | 8.191 μs  | 1.00  | 0.00    | 24.56 KB  | 1.00        | 3.0060 | 0.1831 |
| AterraBuildLifecycle               | Register, build and dispose                  | 14.171 μs | 1.1104 μs | 2.3902 μs | 14.197 μs | 1.73  | 0.33    | 36.86 KB  | 1.50        | 4.5013 | 0.2289 |
| MicrosoftCollection1               | Resolve IEnumerable (01)                     | 21.01 ns  | 0.417 ns  | 0.916 ns  | 21.40 ns  | 1.00  | 0.00    | 56 B      | 1.00        | 0.0067 | 0.0000 |
| AterraCollection1                  | Resolve IEnumerable (01)                     | 184.08 ns | 12.273 ns | 27.451 ns | 172.62 ns | 8.78  | 1.36    | 88 B      | 1.57        | 0.0105 | 0.0000 |
| MicrosoftCollection4               | Resolve IEnumerable (04)                     | 31.67 ns  | 1.418 ns  | 3.142 ns  | 31.56 ns  | 1.00  | 0.00    | 152 B     | 1.00        | 0.0181 | 0.0000 |
| AterraCollection4                  | Resolve IEnumerable (04)                     | 273.99 ns | 6.062 ns  | 13.179 ns | 276.76 ns | 8.74  | 0.98    | 184 B     | 1.21        | 0.0219 | 0.0000 |
| MicrosoftCollection16              | Resolve IEnumerable (16)                     | 71.49 ns  | 3.707 ns  | 8.291 ns  | 71.84 ns  | 1.00  | 0.00    | 536 B     | 1.00        | 0.0641 | 0.0001 |
| AterraCollection16                 | Resolve IEnumerable (16)                     | 958.36 ns | 29.114 ns | 65.117 ns | 960.68 ns | 13.58 | 1.82    | 568 B     | 1.06        | 0.0677 | 0.0000 |
| MicrosoftResolveDeepGraph          | Resolve 8-level transient graph              | 30.33 ns  | 0.833 ns  | 1.863 ns  | 31.14 ns  | 1.00  | 0.00    | 216 B     | 1.00        | 0.0258 | N/A    |
| AterraResolveDeepGraph             | Resolve 8-level transient graph              | 478.31 ns | 9.595 ns  | 21.461 ns | 487.62 ns | 15.83 | 1.21    | 216 B     | 1.00        | 0.0257 | N/A    |
| MicrosoftClosedGeneric             | Resolve generated closed generic closure     | 13.20 ns  | 0.723 ns  | 1.617 ns  | 12.63 ns  | 1.00  | 0.00    | 0 B       | NA          | 0.0000 | N/A    |
| AterraClosedGeneric                | Resolve generated closed generic closure     | 35.15 ns  | 0.851 ns  | 1.886 ns  | 35.73 ns  | 2.70  | 0.35    | 0 B       | NA          | 0.0000 | N/A    |
| MicrosoftResolveMixedGraph         | Resolve mixed graph                          | 35.33 ns  | 0.958 ns  | 2.144 ns  | 36.11 ns  | 1.00  | 0.00    | 144 B     | 1.00        | 0.0172 | N/A    |
| AterraResolveMixedGraph            | Resolve mixed graph                          | 348.11 ns | 18.916 ns | 42.307 ns | 338.15 ns | 9.89  | 1.34    | 144 B     | 1.00        | 0.0172 | N/A    |
| MicrosoftResolveScoped             | Resolve scoped (cached)                      | 23.29 ns  | 0.454 ns  | 1.016 ns  | 23.76 ns  | 1.00  | 0.00    | 0 B       | NA          | 0.0000 | N/A    |
| AterraResolveScoped                | Resolve scoped (cached)                      | 32.68 ns  | 0.783 ns  | 1.751 ns  | 33.10 ns  | 1.41  | 0.10    | 0 B       | NA          | 0.0000 | N/A    |
| MicrosoftResolveSingleton          | Resolve singleton (cached)                   | 11.04 ns  | 0.212 ns  | 0.475 ns  | 11.35 ns  | 1.00  | 0.00    | 0 B       | NA          | 0.0000 | N/A    |
| AterraResolveSingleton             | Resolve singleton (cached)                   | 35.01 ns  | 1.525 ns  | 3.411 ns  | 34.50 ns  | 3.18  | 0.34    | 0 B       | NA          | 0.0000 | N/A    |
| MicrosoftResolveTransient          | Resolve transient                            | 15.94 ns  | 1.067 ns  | 2.365 ns  | 16.60 ns  | 1.00  | 0.00    | 24 B      | 1.00        | 0.0029 | N/A    |
| AterraResolveTransient             | Resolve transient                            | 77.70 ns  | 2.050 ns  | 4.542 ns  | 77.83 ns  | 4.98  | 0.78    | 24 B      | 1.00        | 0.0029 | N/A    |
| MicrosoftOneDecorator              | Resolve one decorator                        | 37.69 ns  | 2.411 ns  | 5.393 ns  | 36.37 ns  | 1.00  | 0.00    | 48 B      | 1.00        | 0.0057 | N/A    |
| AterraOneDecorator                 | Resolve one decorator                        | 142.60 ns | 3.340 ns  | 7.471 ns  | 146.37 ns | 3.86  | 0.55    | 72 B      | 1.50        | 0.0086 | N/A    |
| MicrosoftThreeDecorators           | Resolve three decorators                     | 36.10 ns  | 0.792 ns  | 1.770 ns  | 36.62 ns  | 1.00  | 0.00    | 96 B      | 1.00        | 0.0114 | N/A    |
| AterraThreeDecorators              | Resolve three decorators                     | 560.34 ns | 12.291 ns | 27.491 ns | 570.87 ns | 15.56 | 1.09    | 360 B     | 3.75        | 0.0429 | N/A    |
| MicrosoftDiagnosticsDisabled       | Diagnostics overhead                         | 13.27 ns  | 0.287 ns  | 0.642 ns  | 13.36 ns  | 1.00  | 0.00    | 24 B      | 1.00        | 0.0029 | N/A    |
| DiagnosticsDisabled                | Diagnostics overhead                         | 77.24 ns  | 1.727 ns  | 3.863 ns  | 78.09 ns  | 5.84  | 0.41    | 24 B      | 1.00        | 0.0029 | N/A    |
| DiagnosticsNoOp                    | Diagnostics overhead                         | 243.01 ns | 5.292 ns  | 11.837 ns | 247.36 ns | 18.36 | 1.26    | 728 B     | 30.33       | 0.0868 | N/A    |
| DiagnosticsAllocationMeasurement   | Diagnostics overhead                         | 253.85 ns | 7.912 ns  | 17.697 ns | 253.39 ns | 19.18 | 1.62    | 728 B     | 30.33       | 0.0868 | N/A    |
| MicrosoftAsyncDisposal             | Dispose async resource                       | 96.00 ns  | 2.229 ns  | 4.986 ns  | 97.98 ns  | 1.00  | 0.00    | 424 B     | 1.00        | 0.0507 | 0.0000 |
| AterraAsyncDisposal                | Dispose async resource                       | 744.34 ns | 14.625 ns | 32.711 ns | 756.46 ns | 7.77  | 0.53    | 3776 B    | 8.91        | 0.4511 | 0.0057 |
| MicrosoftDualDisposal              | Dispose dual-interface resource              | 100.62 ns | 4.393 ns  | 9.825 ns  | 98.57 ns  | 1.00  | 0.00    | 424 B     | 1.00        | 0.0507 | 0.0000 |
| AterraDualDisposal                 | Dispose dual-interface resource              | 807.41 ns | 20.829 ns | 46.586 ns | 807.26 ns | 8.10  | 0.88    | 3776 B    | 8.91        | 0.4511 | 0.0057 |
| MicrosoftSyncDisposal              | Dispose sync resource                        | 97.62 ns  | 1.705 ns  | 3.779 ns  | 99.10 ns  | 1.00  | 0.00    | 424 B     | 1.00        | 0.0507 | 0.0000 |
| AterraSyncDisposal                 | Dispose sync resource                        | 749.88 ns | 17.122 ns | 38.296 ns | 748.22 ns | 7.69  | 0.50    | 3704 B    | 8.74        | 0.4425 | 0.0019 |
| MicrosoftDisposableResourceCleanup | Scoped disposable-resource cleanup           | 100.08 ns | 2.695 ns  | 6.028 ns  | 99.90 ns  | 1.00  | 0.00    | 424 B     | 1.00        | 0.0507 | 0.0000 |
| AterraDisposableResourceCleanup    | Scoped disposable-resource cleanup           | 752.37 ns | 17.640 ns | 39.454 ns | 767.21 ns | 7.54  | 0.60    | 3712 B    | 8.75        | 0.4435 | 0.0038 |
| MicrosoftKeyedCollection           | Resolve keyed collection                     | 35.31 ns  | 0.771 ns  | 1.708 ns  | 35.78 ns  | 1.00  | 0.00    | 56 B      | 1.00        | 0.0067 | N/A    |
| AterraKeyedCollection              | Resolve keyed collection                     | 87.74 ns  | 2.139 ns  | 4.785 ns  | 89.33 ns  | 2.49  | 0.18    | 56 B      | 1.00        | 0.0067 | N/A    |
| MicrosoftKeyedCached               | Resolve keyed singleton (cached)             | 25.10 ns  | 0.440 ns  | 0.984 ns  | 25.58 ns  | 1.00  | 0.00    | 0 B       | NA          | 0.0000 | N/A    |
| AterraKeyedCached                  | Resolve keyed singleton (cached)             | 73.77 ns  | 1.574 ns  | 3.520 ns  | 75.07 ns  | 2.94  | 0.18    | 0 B       | NA          | 0.0000 | N/A    |
| MicrosoftKeyedTransient            | Resolve keyed transient                      | 27.88 ns  | 0.488 ns  | 1.092 ns  | 28.28 ns  | 1.00  | 0.00    | 24 B      | 1.00        | 0.0029 | N/A    |
| AterraKeyedTransient               | Resolve keyed transient                      | 85.00 ns  | 1.978 ns  | 4.425 ns  | 86.55 ns  | 3.05  | 0.20    | 24 B      | 1.00        | 0.0029 | N/A    |
| MicrosoftScopeLifecycle            | Create, resolve and dispose scope            | 67.19 ns  | 1.683 ns  | 3.765 ns  | 67.90 ns  | 1.00  | 0.00    | 336 B     | 1.00        | 0.0401 | 0.0000 |
| AterraScopeLifecycle               | Create, resolve and dispose scope            | 653.67 ns | 12.466 ns | 27.882 ns | 662.88 ns | 9.76  | 0.70    | 3552 B    | 10.57       | 0.4244 | 0.0038 |
| MicrosoftAsyncScopeLifecycle       | Create, sync resolve and async dispose scope | 73.12 ns  | 2.027 ns  | 4.535 ns  | 73.74 ns  | 1.00  | 0.00    | 336 B     | 1.00        | 0.0401 | 0.0000 |
| AterraAsyncScopeLifecycle          | Create, sync resolve and async dispose scope | 659.84 ns | 14.280 ns | 31.643 ns | 671.44 ns | 9.06  | 0.71    | 3552 B    | 10.57       | 0.4244 | 0.0038 |
| MicrosoftEmptyScopeCreateDispose   | Empty scope create/dispose                   | 30.02 ns  | 1.172 ns  | 2.573 ns  | 29.48 ns  | 1.00  | 0.00    | 128 B     | 1.00        | 0.0153 | 0.0000 |
| AterraEmptyScopeCreateDispose      | Empty scope create/dispose                   | 54.24 ns  | 3.174 ns  | 6.833 ns  | 51.89 ns  | 1.82  | 0.27    | 104 B     | 0.81        | 0.0124 | 0.0000 |
| MicrosoftScopedCacheCreation       | Scoped cache creation                        | 69.76 ns  | 2.008 ns  | 4.490 ns  | 70.07 ns  | 1.00  | 0.00    | 336 B     | 1.00        | 0.0401 | 0.0000 |
| AterraScopedCacheCreation          | Scoped cache creation                        | 659.57 ns | 16.639 ns | 36.872 ns | 676.28 ns | 9.49  | 0.81    | 3552 B    | 10.57       | 0.4244 | 0.0038 |

```
