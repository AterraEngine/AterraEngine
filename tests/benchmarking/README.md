# Dependency injection benchmarks

These benchmarks compare AterraEngine's generated-activator path with `Microsoft.Extensions.DependencyInjection` using equivalent registrations and lifetimes.

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

Microsoft DI is the baseline in each category, so the ratio reports Aterra's time divided by Microsoft DI's time. MemoryDiagnoser reports managed allocations for both implementations.

Aterra exposes asynchronous resolution and disposal while Microsoft DI resolves synchronously. Resolution benchmarks synchronously consume Aterra's `ValueTask` so the comparison measures each container's public resolution path without adding an artificial task wrapper to Microsoft DI. Run benchmarks outside Rider's debugger on an otherwise idle machine and compare results from the same build and hardware.

## Baseline before resolution-pipeline optimization

The following result is the baseline that motivated the current optimization work. Rerun the complete suite to measure the current implementation on your machine.

```md
BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 8945HS w/ Radeon 780M Graphics 3.99GHz, 1 CPU, 16 logical and 8 physical cores                                                                                                             
Memory: 15.29 GB Total, 1.02 GB Available                                                                                                                                                              
.NET SDK 11.0.100-rc.1.26425.128                                                                                                                                                                       
[Host]     : .NET 11.0.0 (11.0.0-rc.1.26425.128, 11.0.26.42628), X64 RyuJIT x86-64-v4                                                                                                                
DefaultJob : .NET 11.0.0 (11.0.0-rc.1.26425.128, 11.0.26.42628), X64 RyuJIT x86-64-v4


| Method                     | Categories                        | Mean        | Error      | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |---------------------------------- |------------:|-----------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| MicrosoftScopeLifecycle    | Create, resolve and dispose scope |    91.53 ns |   2.486 ns |   6.972 ns |  1.00 |    0.00 | 0.0401 |      - |     336 B |        1.00 |                               
| AterraScopeLifecycle       | Create, resolve and dispose scope |   503.38 ns |   9.989 ns |  19.717 ns |  5.53 |    0.46 | 0.2074 |      - |    1736 B |        5.17 |
|                            |                                   |             |            |            |       |         |        |        |           |             |
| AterraBuildLifecycle       | Register, build and dispose       | 5,085.14 ns | 101.630 ns | 158.226 ns |  0.62 |    0.03 | 1.6403 | 0.0305 |   13736 B |        0.58 |
| MicrosoftBuildLifecycle    | Register, build and dispose       | 8,215.87 ns | 161.248 ns | 350.540 ns |  1.00 |    0.00 | 2.8152 | 0.1755 |   23569 B |        1.00 |
|                            |                                   |             |            |            |       |         |        |        |           |             |
| MicrosoftResolveDeepGraph  | Resolve 8-level transient graph   |    37.81 ns |   0.812 ns |   2.331 ns |  1.00 |    0.00 | 0.0258 |      - |     216 B |        1.00 |
| AterraResolveDeepGraph     | Resolve 8-level transient graph   | 1,121.21 ns |  22.054 ns |  39.202 ns | 29.77 |    2.10 | 0.3052 | 0.0019 |    2568 B |       11.89 |
|                            |                                   |             |            |            |       |         |        |        |           |             |
| MicrosoftResolveMixedGraph | Resolve mixed graph               |    44.42 ns |   0.885 ns |   2.316 ns |  1.00 |    0.00 | 0.0172 |      - |     144 B |        1.00 |
| AterraResolveMixedGraph    | Resolve mixed graph               |   720.25 ns |  14.146 ns |  29.213 ns | 16.26 |    1.08 | 0.2012 |      - |    1688 B |       11.72 |
|                            |                                   |             |            |            |       |         |        |        |           |             |
| MicrosoftResolveScoped     | Resolve scoped (cached)           |    26.37 ns |   0.526 ns |   1.085 ns |  1.00 |    0.00 |      - |      - |         - |          NA |
| AterraResolveScoped        | Resolve scoped (cached)           |   157.08 ns |   3.128 ns |   8.510 ns |  5.97 |    0.41 | 0.0515 |      - |     432 B |          NA |
|                            |                                   |             |            |            |       |         |        |        |           |             |
| MicrosoftResolveSingleton  | Resolve singleton (cached)        |    12.79 ns |   0.251 ns |   0.557 ns |  1.00 |    0.00 |      - |      - |         - |          NA |
| AterraResolveSingleton     | Resolve singleton (cached)        |   155.42 ns |   3.100 ns |   7.185 ns | 12.18 |    0.77 | 0.0525 |      - |     440 B |          NA |
|                            |                                   |             |            |            |       |         |        |        |           |             |
| MicrosoftResolveTransient  | Resolve transient                 |    17.54 ns |   0.350 ns |   0.825 ns |  1.00 |    0.00 | 0.0029 |      - |      24 B |        1.00 |
| AterraResolveTransient     | Resolve transient                 |   195.84 ns |   3.898 ns |   8.878 ns | 11.19 |    0.74 | 0.0658 |      - |     552 B |       23.00 |

```
