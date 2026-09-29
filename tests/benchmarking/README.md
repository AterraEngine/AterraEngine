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

Microsoft DI is the baseline in each category, so the ratio reports Aterra's time divided by Microsoft DI's time.
MemoryDiagnoser reports managed allocations for both implementations.

Aterra exposes asynchronous resolution and disposal while Microsoft DI resolves synchronously. Resolution benchmarks
synchronously consume Aterra's `ValueTask` so the comparison measures each container's public resolution path without
adding an artificial task wrapper to Microsoft DI. Run benchmarks outside Rider's debugger on an otherwise idle machine
and compare results from the same build and hardware.

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


| Method                     | Categories                        | Mean        | Error      | StdDev     | Median      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |---------------------------------- |------------:|-----------:|-----------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| MicrosoftScopeLifecycle    | Create, resolve and dispose scope |    75.27 ns |   1.526 ns |   4.303 ns |    74.57 ns |  1.00 |    0.00 | 0.0401 |      - |     336 B |        1.00 |                 
| AterraScopeLifecycle       | Create, resolve and dispose scope |   695.98 ns |  45.596 ns | 134.440 ns |   659.77 ns |  9.28 |    1.86 | 0.2851 | 0.0019 |    2392 B |        7.12 |
|                            |                                   |             |            |            |             |       |         |        |        |           |             |
| AterraBuildLifecycle       | Register, build and dispose       | 6,588.09 ns | 238.797 ns | 681.300 ns | 6,397.12 ns |  0.71 |    0.09 | 2.0599 | 0.0534 |   17280 B |        0.74 |
| MicrosoftBuildLifecycle    | Register, build and dispose       | 9,294.73 ns | 267.357 ns | 788.309 ns | 9,288.93 ns |  1.00 |    0.00 | 2.7771 | 0.1526 |   23313 B |        1.00 |
|                            |                                   |             |            |            |             |       |         |        |        |           |             |
| MicrosoftResolveDeepGraph  | Resolve 8-level transient graph   |    41.07 ns |   1.478 ns |   4.313 ns |    40.89 ns |  1.00 |    0.00 | 0.0258 |      - |     216 B |        1.00 |
| AterraResolveDeepGraph     | Resolve 8-level transient graph   |   777.97 ns |  21.399 ns |  62.421 ns |   778.64 ns | 19.15 |    2.49 | 0.0257 |      - |     216 B |        1.00 |
|                            |                                   |             |            |            |             |       |         |        |        |           |             |
| MicrosoftResolveMixedGraph | Resolve mixed graph               |    48.08 ns |   1.368 ns |   3.902 ns |    47.44 ns |  1.00 |    0.00 | 0.0172 |      - |     144 B |        1.00 |
| AterraResolveMixedGraph    | Resolve mixed graph               |   459.03 ns |  12.835 ns |  37.236 ns |   461.06 ns |  9.61 |    1.09 | 0.0172 |      - |     144 B |        1.00 |
|                            |                                   |             |            |            |             |       |         |        |        |           |             |
| MicrosoftResolveScoped     | Resolve scoped (cached)           |    27.94 ns |   0.552 ns |   1.493 ns |    28.03 ns |  1.00 |    0.00 |      - |      - |         - |          NA |
| AterraResolveScoped        | Resolve scoped (cached)           |    41.88 ns |   1.055 ns |   3.095 ns |    42.35 ns |  1.50 |    0.14 |      - |      - |         - |          NA |
|                            |                                   |             |            |            |             |       |         |        |        |           |             |
| MicrosoftResolveSingleton  | Resolve singleton (cached)        |    14.47 ns |   0.342 ns |   1.009 ns |    14.42 ns |  1.00 |    0.00 |      - |      - |         - |          NA |
| AterraResolveSingleton     | Resolve singleton (cached)        |    42.74 ns |   1.255 ns |   3.700 ns |    42.41 ns |  2.97 |    0.33 |      - |      - |         - |          NA |
|                            |                                   |             |            |            |             |       |         |        |        |           |             |
| MicrosoftResolveTransient  | Resolve transient                 |    17.10 ns |   0.569 ns |   1.615 ns |    17.00 ns |  1.00 |    0.00 | 0.0029 |      - |      24 B |        1.00 |
| AterraResolveTransient     | Resolve transient                 |   105.87 ns |   3.063 ns |   9.032 ns |   104.85 ns |  6.24 |    0.77 | 0.0029 |      - |      24 B |        1.00 |

```
