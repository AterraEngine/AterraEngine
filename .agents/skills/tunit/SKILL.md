---
name: tunit
description: Write, modify, review, debug, migrate, configure, or design C#/.NET tests using TUnit, TUnit.Assertions, or TUnit.Mocks. Use for TUnit test authoring, data sources, lifecycle, DI, parallelism, execution, reporting, extensibility, and framework migrations; do not use for another test framework unless migrating it to TUnit or using TUnit's standalone assertions/mocks.
---

# TUnit

Use this skill to make TUnit changes that match the installed package and Microsoft.Testing.Platform (MTP), remain safe under default parallel execution, and use TUnit's source-generated, async-first APIs rather than xUnit/NUnit/MSTest assumptions.

This skill was checked against the complete official documentation tree and official repository at commit `c496882cee69be430b67172303aaef2fa26d82b8` (2026-09-29), eight commits after TUnit `1.71.0`; NuGet `1.71.0` was the latest stable release. Treat APIs called out as beta, experimental, obsolete, or version-sensitive accordingly. For a project on another version, inspect its package versions and compile against that version; do not silently upgrade it.

**Authority:** Framework/API statements describe the official documentation and source at that snapshot. Instructions to prefer isolation, narrow constraints, deterministic data, and focused validation are engineering recommendations. When a documentation example conflicts with the installed API, the installed compiler/analyzer and matching release source win. Representative core, data, DI, assertion-generation, mock, context, and reporting patterns were compiled and executed against `1.71.0` on .NET 10.

**Use:** Read the project and core-model sections first, then the relevant feature section and debugging checklist. Snippets showing `sut`, application services, or domain types are focused integration patterns; supply the application's implementation. Separate snippets are not intended to be concatenated into one file. Add ordinary .NET usings as needed; lifecycle interfaces live in `TUnit.Core.Interfaces` and extra assertion enums in `TUnit.Assertions.Enums`.

## First inspect the project

Before choosing syntax or commands:

1. Read the test project, central package management files, `global.json`, target frameworks, language version, and existing TUnit conventions.
2. Identify whether it references the `TUnit` meta package, `TUnit.Engine` plus `TUnit.Assertions`, standalone `TUnit.Assertions`, optional `TUnit.Assertions.Should`, or `TUnit.Mocks` packages.
3. Confirm source-generation mode (default), `[assembly: ReflectionMode]`, or `<EnableTUnitSourceGeneration>false</EnableTUnitSourceGeneration>`.
4. Check repository instructions and existing tests before introducing a fixture, shared lifetime, dependency, category, or custom extension.
5. Preserve package versions unless an upgrade was requested or is necessary and explained.

The `TUnit` meta package configures an executable MTP test project and includes the engine, assertions, code coverage, TRX, and telemetry extensions. It adds global usings for `TUnit.Core`, `TUnit.Assertions`, and `TUnit.Assertions.Extensions`. A manually created project normally has `OutputType` `Exe`, no user `Program.cs`, and no `Microsoft.NET.Test.Sdk`, Coverlet collector, or Coverlet MSBuild package. Those VSTest packages conflict with TUnit.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" Version="1.71.0" />
  </ItemGroup>
</Project>
```

Use `TUnit.Core` rather than `TUnit` for a reusable class library that only defines shared hooks, attributes, base classes, or data sources.

For a new project, `dotnet new install TUnit.Templates` followed by `dotnet new TUnit -n MyTests` supplies the current template. Pin package versions according to repository policy. On .NET Framework, the generator supplies a missing `ModuleInitializerAttribute`; `required`/`init`/records may need a polyfill package. Set `EnableTUnitPolyfills=false` only to resolve competing polyfills. Optional template `--enable-dotcover` supports JetBrains per-test coverage.

## Core model

- A public instance method marked `[Test]` is a test. No class attribute is required. Public synchronous `void`, `Task`/`Task<T>`, and `ValueTask`/`ValueTask<T>` tests are supported; the generator can also adapt supported custom/F# awaitables. `async void` is invalid (`TUnit0031`). Prefer `async Task` for ordinary tests with assertions or async work.
- TUnit source-generates discovery and strongly typed invocation by default. Reflection mode exists for runtime-generated cases such as bUnit/Razor and for F#/VB. Source generation supports single-file deployment and is required for Native AOT.
- Every test invocation gets a new test-class instance. Instance fields do not carry state between test methods or data rows.
- All tests are eligible to run in parallel by default. Design isolation first; constrain only the actual shared resource.
- TUnit assertions build a rule chain and execute only when awaited. An unawaited assertion can let a broken test pass; the assertion analyzer reports it.
- Data sources normally enumerate during discovery, before execution-time initialization. Keep discovery deterministic, fast, and free of fragile external dependencies.
- Prefer independent tests. Use `[DependsOn]` only when the workflow genuinely cannot be isolated.

```csharp
public sealed class CalculatorTests
{
    [Test]
    public async Task Add_ReturnsSum()
    {
        var actual = Calculator.Add(2, 3);
        await Assert.That(actual).IsEqualTo(5);
    }
}
```

## Choose the TUnit mechanism

| Need | Use |
|---|---|
| Fixed compile-time constants | Repeated `[Arguments(...)]` |
| Computed values, objects, tuples, sync/async sequences | Static `[MethodDataSource]`, preferably generic typed form |
| Instance member data in a generic base/test instance | `[InstanceMethodDataSource]`; remember it runs during discovery |
| Managed fixture/resource with initialization, disposal, or sharing | `[ClassDataSource<T>]` |
| Cartesian product of simple per-parameter values | `[MatrixDataSource]` plus `[Matrix]` |
| Cartesian product from mixed data-source kinds | `[CombinedDataSources]` plus a data source on every parameter |
| Per-row name, categories, or skip reason from dynamic data | `TestDataRow<T>` |
| Huge row count that hurts discovery/IDE | `DeferEnumeration = true` |
| Ordinary per-test initialization | constructor for sync work; `[Before(Test)]` for async/contextual work |
| Expensive managed setup shared by scope | class data source with `IAsyncInitializer`/`IAsyncDisposable` |
| Cross-cutting lifecycle behavior | static `[BeforeEvery(...)]` / `[AfterEvery(...)]` or event receiver |
| Exclusive access to one resource | `[NotInParallel("resource-key")]` |
| Bounded concurrency | `[ParallelLimiter<T>]` where `T : IParallelLimit` |
| Parallel phases | `[ParallelGroup("phase")]` |
| Required predecessor test | `[DependsOn]`, preferably `nameof` and generic type-safe overloads |
| Conditional discovery-time skip | custom `SkipAttribute.ShouldSkip` |
| Runtime skip | `Skip.Test(reason)` |
| Cross-cutting body/hook wrapper | `ITestExecutor` / `IHookExecutor` |
| Custom data generation | typed `DataSourceGeneratorAttribute<...>`; async or untyped variant only when needed |

## Authoring and organization

Name a test for observable behavior and its trigger. Keep arrange/act/assert readable, use the existing repository style, and avoid storing results from another test in instance fields. Constructor setup runs once for that invocation because each invocation gets a fresh instance.

Test methods may receive an injected `CancellationToken` plus declared data-source values. In an ordinary test body, obtain context from `TestContext.Current!`; do not add an unsourced `TestContext` method parameter (it produces `TUnit0038` in 1.71). Hooks support context parameters. A failing assertion or unhandled exception fails the test. Use `Skip.Test`/`SkipTestException` for a runtime skip and `InconclusiveTestException` only when no pass/fail conclusion is possible.

Generic test classes or methods require explicit source-generation instantiations:

```csharp
[Test]
[GenerateGenericTest(typeof(int), typeof(string))]
[GenerateGenericTest(typeof(long), typeof(bool))]
public async Task Defaults_AreDefault<T1, T2>()
{
    await Assert.That(default(T1)).IsEqualTo(default(T1));
    await Assert.That(default(T2)).IsEqualTo(default(T2));
}
```

Missing `[GenerateGenericTest]` is diagnosed (`TUnit0058`). C# cannot use an open generic type parameter as an attribute argument; in generic base classes use `[InstanceMethodDataSource]` or place typed attributes on a concrete derived class.

## Assertions

Canonical syntax is `await Assert.That(actual).Assertion(expected)`. TUnit assertions are strongly typed extension methods, so impossible type combinations generally fail at compile time. Awaiting returns the asserted subject (or a specialized result), allowing safe capture from `IsTypeOf<T>`, predicate `Contains`, and `HasSingleItem`.

Use the most specific assertion because its diagnostics include the actual domain values:

```csharp
await Assert.That(count).IsGreaterThan(0); // better than Assert.That(count > 0).IsTrue()

var admin = await Assert.That(users).Contains(x => x.Role == "Admin");
await Assert.That(admin.Email).EndsWith("@example.com");
```

### Assertion catalog

- Equality/reference/comparison: `IsEqualTo`, `IsNotEqualTo`, `IsSameReferenceAs`, `IsNotSameReferenceAs`, `IsGreaterThan`, `IsGreaterThanOrEqualTo`, `IsLessThan`, `IsLessThanOrEqualTo`, `IsBetween`, `IsPositive`, `IsNegative`; use `.Within(tolerance)` for supported numeric and temporal equality and comparer overloads where documented.
- Null/default/boolean: `IsNull`, `IsNotNull`, `IsDefault`, `IsNotDefault`, `IsTrue`, `IsFalse`. Nullable boolean assertions fail for null. Successful null/type assertions participate in nullable flow analysis.
- Strings/StringBuilder: `Contains`, `DoesNotContain`, `StartsWith`, `EndsWith`, `Matches`, `DoesNotMatch`, `IsEmpty`, `IsNotEmpty`, `Length()`; string modifiers include `IgnoringCase()`, `WithComparison(StringComparison...)`, `WithTrimming()`, and `IgnoringWhitespace()`. `WhenParsedInto<T>()` changes the asserted subject to the parsed value. Express null-or-empty as `.IsNull().Or.IsEmpty()`; `Assert.That(string.IsNullOrWhiteSpace(value)).IsTrue()` is appropriate for that combined predicate.
- Regex: `Matches(string|Regex)` and `DoesNotMatch`; after a match, use `.And.Group(nameOrIndex, assertion)` and `.And.Match(index, ...)`. Prefer `[GeneratedRegex]` for stable hot patterns.
- Collections: item/predicate `Contains` and `DoesNotContain`, `Count()`, `IsEmpty`, `IsNotEmpty`, `HasSingleItem`, `All`, `Any`, `IsInOrder`, `IsInDescendingOrder`, `IsOrderedBy`, `IsOrderedByDescending`, `IsEquivalentTo`, `IsNotEquivalentTo`, `HasDistinctItems`. Equivalence uses deep structural comparison and is order-independent by default; use `IsEquivalentTo(expected, CollectionOrdering.Matching)` for ordered results and `.Using(comparerOrPredicate)` for custom equality. Multiplicity matters. `All().Satisfy(item => item...)` supports nested assertions, including a mapper overload. Materialize a side-effecting/lazy enumerable before making repeated assertions. `Count()` is the current numeric chain; obsolete `HasCount`/count shortcuts should not be introduced.
- Dictionaries: `ContainsKey`/`DoesNotContainKey`, `ContainsValue`, collection/count/empty/predicate/equivalence assertions over pairs, keys, or values.
- Exceptions/delegates: `Throws<TException>` accepts derived types, `ThrowsExactly<TException>` requires the exact type, runtime-type `Throws(Type)`, and `ThrowsNothing`. Chain `WithMessage`, `WithMessageContaining`, `WithMessageNotContaining`, `WithMessageMatching`, `WithParameterName`, and `WithInnerException`. Delegate can be sync or async; do not invoke it before passing it to `Assert.That`.
- Tasks/async: task-state assertions (`IsCompleted`, `IsCanceled`, `IsFaulted` and negations; `IsCompletedSuccessfully` where supported), `CompletesWithin`, and `WaitsFor` (alias `Eventually`). Poll a value-producing delegate with an assertion-builder lambda: `await Assert.That(() => queue.Count).WaitsFor(x => x.IsEqualTo(1), timeout: TimeSpan.FromSeconds(2));`. A captured value will not refresh. Task state checks do not wait for completion; timeout assertions do not cancel the underlying work. Own/cancel/await background work explicitly. Await a `ValueTask` only once unless its contract permits reuse.
- Date/time: equality and comparison plus `IsToday`, `IsUtc`, `IsLeapYear`, `IsInFuture`/`IsInPast`, UTC variants, weekend/weekday, daylight-saving checks, DateTimeOffset, DateOnly, TimeOnly, TimeSpan sign/comparison, and DayOfWeek assertions.
- Runtime type/value: `IsTypeOf<T>` is exact; `IsAssignableTo<T>` permits derived/interface compatibility; negative and `IsAssignableFrom` variants exist. `Type` subjects support class/interface, abstract/sealed, value/enum/primitive, public/visible, generic definition/constructed/open, array, by-ref/by-ref-like, pointer, nested visibility, and COM-object checks.
- Specialized: GUID empty/nonempty; HTTP success/client/server/redirection; CancellationToken requested/cancelable; char classification; DirectoryInfo/FileInfo existence and attributes; IP v4/v6; Lazy creation; Stream capabilities; Process/Thread/WeakReference state; URI absolute; UTF-8 Encoding; Version and DayOfWeek.
- Object graph: `.Member(x => x.Property, member => member...)` keeps the parent subject for `.And.Member(...)`; `Satisfies(predicate, expectation)` handles a domain-specific invariant.

String, equality, collection, and date assertions expose specific configuration methods. Inspect IntelliSense/current API for exact option names instead of guessing an NUnit/FluentAssertions modifier.

### Composition and scopes

`.And` requires every rule; `.Or` accepts any rule. Do not mix `.And` and `.Or` in one chain: current TUnit throws `MixedAndOrAssertionsException` and analyzer `TUnitAssertions0001` reports it. Split the assertions or assert one explicit boolean expression. Some older documentation examples mix them; those examples are stale.

Use `Assert.Multiple()` to collect independent failures:

```csharp
using (Assert.Multiple())
{
    await Assert.That(user.Name).IsEqualTo("Alice");
    await Assert.That(user.Age).IsGreaterThanOrEqualTo(18);
}
```

`Assert.Multiple` is for independent checks. A fluent chain describes one combined expectation. Add `.Because("reason")` when the business reason is useful in failure output.

### Async and exception examples

```csharp
[Test]
public async Task InvalidInput_Throws()
{
    var exception = await Assert.That(() => service.SaveAsync(null!))
        .Throws<ArgumentException>()
        .WithParameterName("value");

    await Assert.That(exception.Message).Contains("required");
}

[Test]
public async Task Operation_CompletesPromptly()
{
    await Assert.That(() => service.RefreshAsync())
        .CompletesWithin(TimeSpan.FromSeconds(2));
}
```

### Custom assertions

Prefer source generation for straightforward reusable assertions. A `[GenerateAssertion]` method must be static, live in a partial containing class, take the asserted value first, and return `bool`, `AssertionResult`, `Task<bool>`, or `Task<AssertionResult>`. Give it an `ExpectationMessage`; hide the helper with `[EditorBrowsable(EditorBrowsableState.Never)]`, or use a file-local helper plus `InlineMethodBody = true` for a single return expression. `[AssertionFrom<T>]` adapts existing methods. Generated assertions compose with `.And`/`.Or`.

```csharp
using System.ComponentModel;
using TUnit.Assertions.Attributes;

namespace MyTests;

public static partial class OrderAssertions
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    [GenerateAssertion(ExpectationMessage = "to have a positive total")]
    public static bool HasPositiveTotal(this Order order) => order.Total > 0;
}
```

For full control, derive from `Assertion<T>`, implement `CheckAsync(EvaluationMetadata<T>)` and `GetExpectation()`, then add an extension on `IAssertionSource<T>` that appends to `source.Context.ExpressionBuilder` and passes `source.Context` to the assertion. `EvaluationMetadata` includes `Value` and `Exception`. Extend `IAssertionSource<T>`, not only a concrete assertion type, so continuations work.

For a type-converting assertion, derive from `Assertion<TTo>` and pass `context.Map<TTo>(value => ...)` (sync or async) to its base constructor. Report conversion exceptions from `EvaluationMetadata.Exception`; the mapped value becomes the awaited result and continuation subject. This is how to implement reusable response-body parsing or domain projections. Put generated assertion helpers in a named namespace: a global-namespace helper exposed a generated-code namespace error in the 1.71 validation probe.

`TUnit.Assertions.Should` is an optional beta package (`{version}-beta`) providing awaited `value.Should().BeEqualTo(...)` syntax over the same engine. Default to `Assert.That` unless the repository already chose the Should surface. It can conflict with FluentAssertions at `Should()` and has a few generation limitations; do not introduce it just for stylistic preference.

## Data-driven tests

Data values must match the method/class parameter count and types. Prefer strongly typed tuples, records, generic data-source attributes, and `nameof`; avoid untyped `object[]` unless integrating a dynamic library.

### Arguments

Each method-level `[Arguments]` supplies one row. Multiple attributes supply multiple rows. On a parameter under `[CombinedDataSources]`, one `[Arguments(1, 2, 3)]` supplies three candidate values. `DisplayName`, `Categories`, and `Skip` customize a row; names substitute `$parameterName` or `$arg1`.

```csharp
[Test]
[Arguments(1, 2, 3, DisplayName = "$a + $b = $expected")]
[Arguments(-1, 1, 0, Categories = ["Boundary"])]
public async Task Add(int a, int b, int expected) =>
    await Assert.That(a + b).IsEqualTo(expected);
```

### Method data

Static method sources are source-generation/AOT safe. `[MethodDataSource(nameof(Cases))]` looks on the current class; `[MethodDataSource<Provider>(nameof(Provider.Cases))]` is refactor-friendly across types. Sources may yield a scalar for one parameter, tuples for several parameters, `IEnumerable<T>`, `IAsyncEnumerable<T>`, and factories.

The provider's member is static; the provider type must be non-static when used as a generic type argument. For a static provider class, use `[MethodDataSource(typeof(Provider), nameof(Provider.Cases))]`. Class-level rows feed constructor parameters and combine with method-level rows; repeated sources at the same target supply alternative rows rather than a parameter-wise Cartesian product. Filter invalid data inside the source, or use row `Skip` when the excluded case should remain visible.

For mutable/reference data, return `Func<T>` or `IEnumerable<Func<T>>` so each invocation creates a fresh object. Async sources may accept an optional cancellation token and should use `[EnumeratorCancellation]`. They execute at discovery, so do not make test discovery depend on a slow/unreliable service.

```csharp
public sealed class Cases
{
    public static IEnumerable<Func<(Request Request, int Status)>> Invalid()
    {
        yield return () => (new Request(""), 400);
        yield return () => (new Request("bad"), 422);
    }
}

[Test]
[MethodDataSource<Cases>(nameof(Cases.Invalid))]
public async Task RejectsInvalid(Request request, int status)
{
    var response = await sut.SendAsync(request);
    await Assert.That((int)response.StatusCode).IsEqualTo(status);
}
```

Use `[InstanceMethodDataSource]` only when an instance member is necessary. It is evaluated during discovery. `IAsyncInitializer` has not run then; use predefined values or, only when discovery truly requires loaded data, `IAsyncDiscoveryInitializer`.

### Class data and sharing

`[ClassDataSource<T>]` creates and injects `T` into a class constructor, test method, or required property. `T` needs a public parameterless constructor. The data-source type itself does not receive constructor injection; give nested dependencies data-source-decorated required properties.

`SharedType` controls lifetime:

| Value | Instance and disposal scope |
|---|---|
| `None` (default) | Injection/test scope; disposed after use |
| `PerClass` | One per test class; disposed after its last test |
| `PerAssembly` | One per assembly |
| `PerTestSession` | One across the whole run/assemblies |
| `Keyed` | Shared by matching `Key`; disposed after all users finish |

Shared fixtures may be accessed concurrently. Keep them immutable/thread-safe or add a precise parallel constraint. `IAsyncInitializer.InitializeAsync()` runs after dependencies are injected and before the body; `IDisposable`/`IAsyncDisposable` are tracked automatically. Nested dependencies initialize depth-first and dispose in reverse/ref-counted scope order.

`None` means a fresh instance for each injection point, not one automatically shared across all parameters/properties in a test. A shared fixture initializes in the context of its first consuming test; do not retain that context as the identity of every later user. `IKeyedDataSource.Key` receives a keyed source's key before initialization. Model nested dependencies explicitly; sibling-property initialization order is not a contract, and dependency cycles fail.

```csharp
public sealed class DatabaseFixture : IAsyncInitializer, IAsyncDisposable
{
    public Task InitializeAsync() => StartAsync();
    public ValueTask DisposeAsync() => StopAsync();
}

[ClassDataSource<DatabaseFixture>(Shared = SharedType.PerTestSession)]
public sealed class RepositoryTests(DatabaseFixture database)
{
    [Test]
    public async Task ReadsRow() =>
        await Assert.That(await database.CountAsync()).IsGreaterThanOrEqualTo(0);
}
```

Multi-type `ClassDataSource<T1,...>` uses positional `Shared = [...]` and `Keys = [...]`; keyed positions need the corresponding key. Do not shift the key array.

### Matrix and combined data

`[MatrixDataSource]` takes `[Matrix(...)]` on every parameter and produces the Cartesian product. Empty `[Matrix]` expands every `bool` or enum value. `[MatrixRange<T>(min, max)]` uses step 1; its third constructor argument sets the step. `[MatrixMethod<TClass>(nameof(Source))]` reads an `IEnumerable<T>` member. Repeated method-level `[MatrixExclusion(value1, value2, ...)]` removes exact generated tuples.

`[CombinedDataSources]` similarly produces a Cartesian product but permits any `IDataSourceAttribute` on each parameter, including multiple sources whose values are unioned. Every parameter needs a source, and method-level sources should not be mixed into the same combined test. Calculate row counts before using either.

```csharp
[Test, CombinedDataSources]
public async Task ParsesAcrossModes(
    [Arguments("1", "01")] string input,
    [MethodDataSource<ModeCases>(nameof(ModeCases.All))] ParseMode mode)
{
    await Assert.That(Parser.TryParse(input, mode)).IsTrue();
}
```

```csharp
[Test, MatrixDataSource]
[MatrixExclusion(1, 1)]
public async Task Adds(
    [MatrixRange<int>(1, 3)] int left,
    [MatrixMethod<MatrixCases>(nameof(MatrixCases.Values))] int right)
{
    await Assert.That(left + right).IsPositive();
}
```

### Per-row metadata and deferred enumeration

Wrap dynamic rows in `TestDataRow<T>` with `DisplayName`, `Skip`, and `Categories`. A wrapped `Func<T>` is invoked per test and tuple results spread across parameters.

```csharp
public static IEnumerable<TestDataRow<(int Input, int Expected)>> Rows()
{
    yield return new((2, 4), DisplayName: "Double $arg1", Categories: ["Smoke"]);
    yield return new((0, 0), DisplayName: "Zero", Skip: "Pending boundary contract");
}
```

`DeferEnumeration = true` on any source defers all case expansion until execution. Discovery shows one container node; runtime children carry actual results and the container aggregates them. Consequences: individual rows cannot be IDE-selected/filtered or targeted by `[DependsOn]`, and flat TRX/console counts include one extra container result. Use it for genuinely large sources, not ordinary parameterization.

## Dependency injection and construction

TUnit does not expose user services through `TestContext`. It supplies construction extension points:

- `[ClassDataSource<T>]` handles ordinary fixtures and nested property dependencies.
- `IClassConstructor` plus `[ClassConstructor<T>]` controls creation of the test class. `Create(Type, ClassConstructorMetadata)` receives data-source/metadata context. Each test gets its own constructor attribute instance, so per-test state is safe there.
- `DependencyInjectionDataSourceAttribute<TScope>` integrates any container. Override `CreateScope(DataGeneratorMetadata)` and `Create(TScope, Type)`. Apply the custom attribute to the test class; TUnit resolves constructor parameters and manages the scope lifecycle.
- Required-property injection supports `MethodDataSource`, `ClassDataSource<T>`, and typed generator attributes. A generator on a property supplies its first value; it does not parameterize the whole test for every yielded row. For a service provider, create a property-compatible custom `DependencyInjectionDataSourceAttribute<TScope>`.

```csharp
public sealed class MicrosoftDiAttribute
    : DependencyInjectionDataSourceAttribute<IServiceScope>
{
    private static readonly ServiceProvider Root = new ServiceCollection()
        .AddScoped<IClock, FakeClock>()
        .BuildServiceProvider();

    public override IServiceScope CreateScope(DataGeneratorMetadata metadata) =>
        Root.CreateScope();

    public override object? Create(IServiceScope scope, Type type) =>
        scope.ServiceProvider.GetService(type);

    [After(TestSession)]
    public static ValueTask DisposeRoot() => Root.DisposeAsync();
}

[MicrosoftDi]
public sealed class ServiceTests(IClock clock)
{
    [Test]
    public async Task UsesClock() => await Assert.That(clock).IsNotNull();
}
```

Do not describe TUnit as automatically registering arbitrary application services, selecting Microsoft DI lifetimes, or providing general method-parameter service injection. The custom attribute/container owns those choices.

In this Microsoft DI pattern, transient services are created per resolution, scoped services per created test scope, and singleton services per root provider. Scope disposal does not dispose the root provider, hence the session hook. Use an async-disposable scope when services require async disposal. Method arguments can receive fixture data through method-level data-source attributes; that is distinct from automatic service-container resolution.

## Lifecycle and hooks

The practical execution order is:

1. Discovery hooks; scan tests; create/enumerate data sources; inject discovery properties; run `IAsyncDiscoveryInitializer`; after-discovery hook; registration receivers.
2. Before session, assembly, and class scopes.
3. For each test: construct a fresh test instance; set cached injected properties; initialize tracked objects and the test instance; run global/early/before-test events and hooks; execute the body; run early/after-test/late/global cleanup; dispose the test instance; release tracked objects.
4. Last-test class, assembly, and session events, then after hooks while scopes unwind.

Exact per-test ordering:

```text
constructor
→ injected properties
→ IAsyncInitializer
→ [BeforeEvery(Test)]
→ ITestStartEventReceiver(Early)
→ [Before(Test)]
→ ITestStartEventReceiver(Late)
→ body
→ ITestEndEventReceiver(Early)
→ [After(Test)]
→ ITestEndEventReceiver(Late)
→ [AfterEvery(Test)]
→ IDisposable/IAsyncDisposable
→ release tracked data-source objects
```

Hook table:

| Scope | Before/after | Static? | Context |
|---|---|---:|---|
| Discovery | `[Before(TestDiscovery)]`, `[After(TestDiscovery)]` | yes | `BeforeTestDiscoveryContext` / `TestDiscoveryContext` |
| Session | `[Before(TestSession)]`, `[After(TestSession)]` | yes | `TestSessionContext` |
| Assembly | `[Before(Assembly)]`, `[After(Assembly)]` | yes | `AssemblyHookContext` |
| Class | `[Before(Class)]`, `[After(Class)]` | yes | `ClassHookContext` |
| Test | `[Before(Test)]`, `[After(Test)]` | no | `TestContext` |
| Every test/class/assembly | `[BeforeEvery(...)]`, `[AfterEvery(...)]` | yes | matching context |

Hooks may return `void`, `Task`, or `ValueTask`, never `async void`. They may accept no parameters, the scope context, `CancellationToken`, or context then token. In setup hooks, pass the injected hook token: it includes the hook timeout. `context.Execution.CancellationToken` is the test token and does not include the setup-hook timeout; analyzer `TUnit0075` warns about this mistake.

Base `[Before(Test)]` hooks run before derived hooks. Derived `[After(Test)]` hooks run before base cleanup. Before-hook failures stop the body; all after hooks and disposal still run, collect their exceptions, and report them together. Prefer multiple `[After]` methods when independent cleanup steps must all run.

Within a scope, hook attributes expose `Order` (lower values first), e.g. `[Before(Test, Order = 1)]`. Keep inheritance order and scope nesting separate from this local ordering. For `AsyncLocal` values set in a before hook, call the hook context's `AddAsyncLocalValues()` to propagate them into subsequent framework/test work. Do not assume ordinary async caller/callee flow propagates mutations back upward.

Use constructors for cheap synchronous per-test initialization, hooks for async/context-aware setup, and class data sources for resources whose ownership/lifetime matters. `IAsyncDiscoveryInitializer` inherits the initialization shape but runs during discovery; use it only to produce test cases. Discovery can occur often in an IDE.

```csharp
public sealed class ApiTests
{
    private HttpClient? _client;

    [Before(Test), Timeout(10_000)]
    public async Task SetUp(CancellationToken cancellationToken)
    {
        _client = await ClientFactory.CreateAsync(cancellationToken);
    }

    [After(Test)]
    public void TearDown(TestContext context)
    {
        if (context.Execution.Result?.State == TestState.Failed)
            context.Output.WriteLine("Capturing failure diagnostics");
        _client?.Dispose(); // Setup may have failed before assignment.
    }
}
```

Event receiver interfaces support behavior attached to a test class, custom constructor, injected class/method argument, injected property, or attribute. Current lifecycle receivers include discovery/registered, first/last session/assembly/class, test start/end/skipped/retry. `ITestStartEventReceiver` and `ITestEndEventReceiver` can choose `EventReceiverStage.Early` or `Late` on .NET 8+; older targets use late behavior. Each applied attribute instance is constructed per test. Use events for reusable object-owned behavior; use hooks for straightforward suite code.

## Test context, output, logging, and artifacts

During execution use the hook's injected context or `TestContext.Current` in a test body. During discovery/data generation use `TestBuilderContext.Current` or the builder context on `DataGeneratorMetadata`. Do not expect `TestContext.Current` in unrelated background callbacks; capture the context/execution object or explicitly propagate it.

The current interface-organized API is:

- `context.Execution`: phase, nullable result, cancellation, timestamps, retry attempt, skip reason, result overrides, linked tokens, custom hook executor, and reporting/discoverability controls.
- `context.Metadata`: definition ID, `TestDetails`, test name, display name/formatter, method/class/arguments, attributes and custom properties.
- `context.Parallelism`: constraints, execution priority, and read-only limiter. Set a limiter during registration through `TestRegisteredContext.SetParallelLimiter`.
- `context.Dependencies`: declared details/relationship and `GetTests(...)` returning `IReadOnlyList<TestContext>`.
- `context.StateBag`: concurrent key/value storage with `Items`, indexer, `GetOrAdd`, typed `TryGetValue`, and remove operations.
- `context.Events`: lazy nullable async events.
- `context.Output`: standard/error output, captured text, and artifacts.
- Direct `context.Id`: one invocation. `context.Metadata.DefinitionId`: common definition across data rows/retries.
- `TestContext.Parameters`: session-wide `IReadOnlyDictionary<string,List<string>>` from repeated `--test-parameter key=value`. Repeating a key appends values; only the first `=` splits the pair. Available to tests, hooks, and sources; use `TryGetValue` for optional parameters.
- `TestContext.Configuration.Get("Nested:Key")`: string or null from the platform's `{AppName}.testconfig.json`.
- `TestContext.ResultsDirectory`: absolute configured results directory.
- `context.Isolation`: `UniqueId`, `GetIsolatedName(baseName)`, and `GetIsolatedPrefix(separator)` for parallel-safe external resource names.

Use `TestBuilderContext.StateBag` to carry discovery data into the resulting `TestContext`. Its `TestMetadata`, `Events`, `DataSourceAttribute`, and `DefinitionId` are discovery-time constructs.

```csharp
[Test, Property("Category", "Integration")]
public async Task EmitsDiagnostics()
{
    var context = TestContext.Current!;
    context.Output.WriteLine($"Running {context.Metadata.DisplayName}");
    var file = Path.Combine(TestContext.ResultsDirectory, $"{context.Id}.json");
    await File.WriteAllTextAsync(file, "{}");
    context.Output.AttachArtifact(file, "Captured state", "State at assertion time");
}
```

`Console.Write/WriteLine` is captured and correlated through the current async context. `context.Output` is clearer; `OutputWriter` remains valid for direct `TextWriter` access. For cross-thread callbacks in the test process, propagate `context.Id`, resolve `TestContext.GetById(id)`, and wrap work in `using (context.MakeCurrent())`; always null-check because a completed test is removed. `GetById` does not locate contexts in another process; use propagated IDs/trace context and an integration bridge there. OpenTelemetry W3C activity/baggage can correlate automatically on .NET 8+ when the TUnit activity source has a listener.

Test-level artifacts attach with `context.Output.AttachArtifact(path|Artifact)`. Session-level artifacts attach through the documented session context. Generate files in `TestContext.ResultsDirectory`, use unique names under parallel execution, attach after the file exists, and avoid deleting them during cleanup.

Logging options:

- `TestContext.Current!.GetDefaultLogger()` supports TUnit log levels.
- Custom sinks implement `ILogSink` (`IsEnabled`, `Log`, `LogAsync`) and register before discovery using `TUnitLoggerFactory.AddSink(...)`; disposable sinks are closed at session end.
- `TUnit.Logging.Microsoft` bridges `Microsoft.Extensions.Logging` outside ASP.NET; `TUnit.AspNetCore` includes the bridge for its factory.
- `--output Detailed` exposes passing-test detail; `Normal` reduces output. Exact presets/capture depend on the installed MTP version: the checked MTP 2.4.1 also provides `Minimal`, `--show-test-results`, `--show-stdout`, and `--show-stderr`. Use the test executable's `--help`, especially when a documentation page describes older buffering behavior. `TUNIT_ENABLE_IDE_STREAMING=1` opts into IDE live streaming but is off by default because some MTP/IDE combinations can crash.

## Parallelism, isolation, and execution

Assume two arbitrary test invocations overlap. Static fields, environment variables, current directory, fixed ports/files, shared database rows, global culture, and singleton fixtures are shared state. Prefer unique resources (`context.Isolation`), transactions/namespaces, immutable fixtures, and thread-safe fakes.

TUnit constraints and shared lifetimes coordinate one test process/session, not independent `dotnet test` project processes or CI jobs. Isolation helpers use an in-process counter; add a run/process identifier when multiple processes share external resources. For cross-process exclusivity, use isolation or an actual external lock rather than relying on the same TUnit constraint string.

### Constraints

- `[NotInParallel]` with no keys makes a test run completely alone.
- `[NotInParallel("db")]` serializes only tests sharing any matching key; unrelated keys may overlap. Prefer this narrow form.
- `[ParallelGroup("database-phase")]` batches classes into phases: tests inside a group still run in parallel, but a different group does not overlap. Ungrouped tests follow normal rules.
- `[ParallelLimiter<MyLimit>]` caps concurrent tests sharing the limiter type. `IParallelLimit.Limit` is the actual number of tests, not a thread count. Attribute precedence is method > class > assembly; an explicit attribute beats a programmatic limiter.
- `--maximum-parallel-tests N`, `TUNIT_MAX_PARALLEL_TESTS`, or discovery-time `context.Settings.Parallelism.MaximumParallelTests` caps the whole run. Built-in default is four times CPU count; environment `0` means unlimited, while programmatic `null` requests the default heuristic.
- `[assembly: NotInParallel]` makes the assembly sequential. Use it only when the whole suite truly shares an indivisible resource.

```csharp
public sealed record DatabaseLimit : IParallelLimit
{
    public int Limit => 4;
}

[ParallelLimiter<DatabaseLimit>]
public sealed class RepositoryTests
{
    [Test, NotInParallel("schema-migrations")]
    public Task AppliesMigration() => MigrateAsync();
}
```

Avoid `.Wait()`, `.Result`, and `.GetAwaiter().GetResult()` in tests/hooks. TUnit uses the standard thread pool; blocking or excessive CPU `Task.Run` work can starve continuations across concurrent tests. Await I/O directly and use a limiter for CPU/resource-heavy tests.

## Dependencies and ordering

`[DependsOn(nameof(Other))]` waits for every matching predecessor to finish while unrelated graph branches retain parallelism. If overloaded, provide the parameter type array. Prefer generic typed dependency attributes where available for refactor safety. Multiple attributes create multiple incoming edges.

By default a failed predecessor prevents its dependent from starting. `ProceedOnFailure = true` permits the dependent while preserving the original failure. Access predecessor contexts via `TestContext.Current!.Dependencies.GetTests(...)`; data-driven predecessors return all invocations, so select by arguments/metadata rather than `.First()` unless exactly one is guaranteed.

Ordering is configured with the `Order` property of `[NotInParallel]`, for example `[NotInParallel("workflow", Order = 1)]`; there is no standalone `[Order]` attribute in the checked source. Tests sharing the constraint run smaller orders first. Prefer dependencies because they state the relationship and preserve unrelated parallel work. A dependency crossing incompatible parallel groups/limiters has no ordering guarantee; keep a dependency graph within compatible execution constraints. Keep the graph acyclic and verify a focused run of a dependent test, not just the full suite.

Do not use dependencies to pass ordinary setup results. A class data source or one test with several acts/assertions is usually more maintainable. If a workflow must pass state, store it in the predecessor `StateBag` and account for multiple data rows.

## Cancellation, timeout, retry, and repeat

`[Timeout(milliseconds)]` applies at method, class, or assembly level with method > class > assembly. The injected test `CancellationToken` is linked to the attempt timeout. Forward it to every cancellable operation. Cancellation is cooperative; TUnit cannot stop code that ignores the token. Each retry gets a fresh timeout window.

`TestContext.Current!.Execution.Cancel()` cancels only the current test attempt and marks it cancelled. Capture the `Execution` interface before registering callbacks on other threads. Cleanup hooks receive `CancellationToken.None` after test cancellation so cleanup can finish. External tokens can be linked with `Execution.AddLinkedCancellationToken`.

```csharp
[Test, Timeout(30_000)]
public async Task ProcessesMessages(CancellationToken cancellationToken)
{
    await processor.RunAsync(cancellationToken);
    await Assert.That(processor.Completed).IsTrue();
}
```

`[Retry(N)]` permits up to N additional attempts after failure and stops after success. Basic retry handles any exception. Current 1.71 configuration includes `RetryOnExceptionTypes = [typeof(HttpRequestException), ...]`, `BackoffMs`, and `BackoffMultiplier` (delay is `BackoffMs * multiplier^(attempt-1)`). For conditions beyond exception assignability, derive from `RetryAttribute` and override `Task<bool> ShouldRetry(TestContext, Exception, int currentRetryCount)`. Apply method/class/assembly with specific scope winning. Retries should cover a known transient boundary, preserve attempt diagnostics, and never hide deterministic assertion, race, or product failures.

`[Repeat(N)]` always creates N additional invocations (total N+1), regardless of result; use it for consistency/stress checks. It has the same scope precedence as retry. Do not confuse repeat count with total runs.

## Skip, explicit, categories, metadata, culture

- `[Skip("reason")]` works on method/class/assembly. Derive from `SkipAttribute` and override `ShouldSkip(TestRegisteredContext)` for reusable discovery-time platform/runtime conditions.
- `[RunOn(OS.Windows | OS.Linux)]` and `[ExcludeOn(OS.MacOs | OS.Browser)]` are current built-in OS-gating `SkipAttribute` implementations. Use them instead of recreating common OS checks.
- `Skip.Test("reason")` works inside a body or hook when the condition is available only at runtime.
- `[Explicit]` works on method or class. It runs only when every selected test is explicit; a filter mixing explicit and ordinary tests excludes explicit tests. Select the explicit method/class/category exclusively.
- `[Category("Smoke")]` supplies filterable Category metadata. `[Property("Owner", "Backend")]` adds arbitrary string metadata and inherits from base classes. Data rows can add categories individually.
- `[Culture("de-AT")]` at method/class/assembly temporarily sets current culture and UI culture and restores them. It accepts one culture per scope; use separate tests for multiple cultures.
- `[DisplayName("... $parameterName ...")]` changes test display names. Derive from `DisplayNameFormatterAttribute` for logic and `ArgumentDisplayFormatter` plus `[ArgumentDisplayFormatter<T>]` for complex argument rendering.
- `[NotDiscoverable]` hides helper/internal tests from discovery (and can be subclassed with `ShouldHide`); `[ExecutionPriority(Priority.High|Normal|Low)]` changes scheduling priority. Use either only for an explicit framework-level need, since hidden tests and priority scheduling can surprise users.

## Running, discovery, and filtering

TUnit is an executable MTP application. Prefer `dotnet test` for normal repository validation because it runs every target framework. `dotnet run --project ...` is convenient for one project/TFM and direct application flags. A built DLL can run with `dotnet path/to/Tests.dll`; a published executable runs directly.

For **.NET 10+ native MTP mode**, merge this into the existing `global.json` (preserve its SDK configuration):

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

```powershell
dotnet build .\tests\MyTests\MyTests.csproj
dotnet test --project .\tests\MyTests\MyTests.csproj
dotnet test --project .\tests\MyTests\MyTests.csproj --list-tests
dotnet test --project .\tests\MyTests\MyTests.csproj --treenode-filter "/*/*/CalculatorTests/Add_ReturnsSum"
```

Native MTP mode requires MTP 1.7+ and uses `--project`, `--solution`, or `--test-modules`, not a positional project/solution/DLL. SDK version alone does not select the runner. MTP flags can be passed directly; `--` remains useful to disambiguate application arguments. .NET 11 Preview 6+ also permits `DOTNET_TEST_RUNNER=Microsoft.Testing.Platform`, overriding `global.json`; this is preview-specific guidance.

**Legacy `dotnet test` integration:** with an older SDK/compatible MTP MSBuild bridge, use `dotnet test <project> -- --treenode-filter "..."`. `TestingPlatformDotnetTestSupport=true` enables this bridge (check package-provided defaults). MTP 2 removes legacy integration on .NET 10+, so do not recommend it for a current .NET 10 project. `TestingPlatformCaptureOutput=false` and `TestingPlatformShowTestsFailure=true` are legacy bridge settings, not native MTP settings.

**Runner-independent direct execution:** `dotnet run --project <project> -- --treenode-filter "..."`. Specify `--framework` for a multi-target project. Inspect `global.json`, `dotnet test --help`, and the repository's existing commands before selecting a mode.

Tree filter shape is `/Assembly/Namespace/Class/Test[Property=Value]`. Use `*` within a segment and terminal `/**` for any remaining depth. Use `=`/`!=`; combine parenthesized expressions with `&` or `|`. Only one `[...]` property group is allowed per segment.

```powershell
# Class
dotnet test --treenode-filter "/*/*/LoginTests/*"
# Category and priority
dotnet test --treenode-filter "/**[(Category=Smoke)&(Priority=High)]"
# Either class
dotnet test --treenode-filter "/*/*/(LoginTests)|(SignupTests)/*"
```

Never substitute VSTest `--filter`; TUnit/MTP rejects it and can misleadingly report zero tests. One older `TestDataRow` doc example uses `--filter`; the current filter reference and engine use `--treenode-filter`.

Useful execution flags include `--list-tests`, `--minimum-expected-tests`, `--treenode-filter`, `--maximum-parallel-tests`, `--timeout 10m`, `--fail-fast`, `--output Normal|Detailed`, `--log-level`, `--detailed-stacktrace`, `--reflection`, `--test-parameter key=value`, `--results-directory`, `--diagnostic` and its output/verbosity controls, and `--no-ansi`. Use `--minimum-expected-tests` in CI where zero discovery must fail clearly. Current MTP 2.4.1 prefers `--progress off` over deprecated `--no-progress`, supports `--list-tests json`, `--filter-uid`, `--zero-tests-policy`, `--show-flaky-tests`, `--show-slowest-tests`, and `--config-file`. Check executable help before using these in a project on an older platform.

For repeated flaky/race detection, run the same tree filter many times using repository-approved scripting or add a temporary `[Repeat]` only if changing discovery/results is acceptable. Exercise the normal parallel configuration; a sequential-only pass cannot validate thread safety.

## Configuration and precedence

Documented precedence for the same setting is:

1. command-line flag;
2. environment variable;
3. `context.Settings` set in `[Before(TestDiscovery)]`;
4. built-in default.

Programmatic groups include `Timeouts`, `Parallelism`, `Execution`, `Display`, `Reporting`, and, with TUnit.Mocks, `Mocks`:

```csharp
public static class TestConfiguration
{
    [Before(TestDiscovery)]
    public static void Configure(BeforeTestDiscoveryContext context)
    {
        context.Settings.Timeouts.DefaultTestTimeout = TimeSpan.FromMinutes(5);
        context.Settings.Timeouts.DefaultHookTimeout = TimeSpan.FromMinutes(2);
        context.Settings.Parallelism.MaximumParallelTests = 8;
        context.Settings.Execution.FailFast = false;
        context.Settings.Display.DetailedStackTrace = false;
        context.Settings.Reporting.HtmlReportEnabled = true;
        context.Settings.Reporting.JsonReportEnabled = true;
    }
}
```

Defaults documented at the research snapshot: test timeout 30 minutes; hook timeout 5 minutes; forceful exit grace 30 seconds; process-exit hook delay 500 ms; maximum parallelism null/default heuristic; fail-fast false; detailed internal stack false; HTML and JSON reports true; artifact upload true.

Important environment variables:

- `TUNIT_MAX_PARALLEL_TESTS`, `TUNIT_EXECUTION_MODE=sourcegeneration|aot|reflection`.
- `TUNIT_DISABLE_LOGO`, `TUNIT_DISCOVERY_DIAGNOSTICS=1`, `TUNIT_DIAGNOSTIC_CAST=true`, `TUNIT_ENABLE_IDE_STREAMING=1`.
- `TUNIT_DISABLE_GITHUB_REPORTER`, `TUNIT_GITHUB_REPORTER_STYLE=collapsible|full`.
- `TUNIT_DISABLE_HTML_REPORTER`, `TUNIT_DISABLE_JSON_REPORT`, `TUNIT_DISABLE_ARTIFACT_UPLOAD`, `TUNIT_ARTIFACT_RETENTION_DAYS` (positive integer for automatic CI upload retention).
- `TUNIT_ENABLE_JUNIT_REPORTER`, `TUNIT_DISABLE_JUNIT_REPORTER`, `JUNIT_XML_OUTPUT_PATH`.
- `TUNIT_AGGREGATE_REPORTS`, `TUNIT_AGGREGATE_DIR` for multi-project report aggregation.
- Platform variables such as `TESTINGPLATFORM_TELEMETRY_OPTOUT` and `TESTINGPLATFORM_UI_LANGUAGE`.

The platform configuration file is named `{AppName}.testconfig.json`, even though documentation sometimes says `testconfig.json` as shorthand. `TestContext.Configuration.Get` returns strings/null and uses colon-delimited nested keys. TUnit does not document `.runsettings` as its configuration system; do not carry VSTest `.runsettings` assumptions into MTP. Some platform options can be supplied with `<TestingPlatformCommandLineArguments>` in MSBuild.

Ensure the configuration file reaches the test executable's output/publish directory. For example, add `<None Update="MyTests.testconfig.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />` to an item group. Use configuration for application values; do not assume arbitrary JSON keys are mapped onto `context.Settings`. `--coverage-settings` is the coverage extension's XML file, distinct from VSTest run settings. In mixed-framework solutions, route TUnit-only flags to TUnit projects; another MTP framework may reject `--treenode-filter`.

Source generation is default. Enable reflection at runtime with `--reflection`, per assembly with `[assembly: ReflectionMode]`, or through `TUNIT_EXECUTION_MODE`; an AOT platform check has highest priority, then command flag, assembly attribute, environment, default. Only set `<EnableTUnitSourceGeneration>false>` when a project always uses reflection mode. Verbose generator diagnostics use `.editorconfig` `tunit.enable_verbose_diagnostics = true` or `<TUnitEnableVerboseDiagnostics>true</...>`.

## Reporting, coverage, and CI

Only claim formats current TUnit/MTP supports:

- Console/MTP result stream is always present.
- HTML report is generated by default at `TestResults/{AssemblyName}-{os}-{tfm}-report.html`; the OS/TFM suffix prevents matrix-build collisions (some reference pages omit it). `--report-html` is obsolete/no-op compatibility behavior for TUnit's reporter. Disable through the reporting setting or `TUNIT_DISABLE_HTML_REPORTER`. `--report-html-filename` is the TUnit option without Microsoft's HTML extension; with that extension use `--tunit-report-html-filename`.
- A machine-readable `.tunit-report.json` sidecar is generated with HTML by default for aggregation. The documentation lists `--output-json` and filename/prefix flags, but they are not registered in the checked 1.71 executable/source wiring. Use the supported sidecar for JSON results; do not promise these flags unless the project's executable advertises them. MTP `--list-tests json` is discovery output, not an execution report.
- TRX comes with the `TUnit` meta package: `--report-trx` and `--report-trx-filename`.
- JUnit XML is a native current reporter: enable with `TUNIT_ENABLE_JUNIT_REPORTER=true` (or automatic GitLab CI detection), then optionally set `--junit-output-path path`. The path flag alone does not enable it. `TUNIT_DISABLE_JUNIT_REPORTER` wins; at 1.71 these enable/disable switches test variable presence, so unset the disabling variable rather than setting it to `false`. Output-path precedence is CLI > `JUNIT_XML_OUTPUT_PATH` > `TestResults/{AssemblyName}-junit.xml`. Some general CI pages still recommend TRX conversion or mention `--report-junit`; prefer the verified engine options above.
- Coverage comes from `Microsoft.Testing.Extensions.CodeCoverage`: `--coverage`, `--coverage-output`, `--coverage-output-format cobertura|xml`, and `--coverage-settings`. The command reference also lists the native `.coverage` format. Never add Coverlet.
- GitHub Actions reporter activates from `GITHUB_ACTIONS` plus `GITHUB_STEP_SUMMARY`, defaults to collapsible, lists only nonpassing details, and respects GitHub's 1 MB limit. Azure DevOps can ingest TRX; other CI systems can ingest TRX or native JUnit according to their tooling.

```powershell
dotnet test --project .\tests\MyTests\MyTests.csproj --report-trx --coverage --coverage-output-format cobertura
$env:TUNIT_ENABLE_JUNIT_REPORTER = "true"
dotnet test --project .\tests\MyTests\MyTests.csproj --junit-output-path .\TestResults\tests.junit.xml
```

HTML reports include filtering, failure detail, output/artifacts, retry/flaky information, and distributed trace timelines where activities exist. TUnit emits test spans on `TUnit` and lifecycle spans on `TUnit.Lifecycle` (.NET 8+). `TUnit.OpenTelemetry` can auto-configure an OTLP exporter/receiver; manual setups subscribe to both sources. Use `tunit.test.id`, `tunit.test.node_uid`, `tunit.session.id`, class, and assembly tags for correlation. The HTML report can receive registered out-of-process traces through the TUnit OTLP receiver; register trace IDs with `TestContext.Current!.RegisterTrace(...)` and note the per-test external span cap.

For multiple projects, HTML JSON sidecars can aggregate cooperatively when a shared `TUNIT_AGGREGATE_DIR` exists; this is automatic on GitHub Actions. For separate steps/jobs use `TUNIT_AGGREGATE_REPORTS=defer`, collect `*.tunit-report.json`, then install `TUnit.Reporting.Tool` and run:

```text
tunit-report merge --directory <dir> --output merged-report.html --github-summary [--fail-on-failures]
```

Disabling HTML also removes the pipeline that writes aggregation sidecars. Disabling JSON prevents aggregation. Preserve failing-job artifacts with `if: always()` in CI.

Automatic GitHub artifact upload also needs the Actions runtime token/results URL; shell test steps do not receive them automatically. Prefer an explicit artifact-upload step when that is how the repository already publishes reports. Keep filenames unique per project/OS/TFM, and preserve the test command's nonzero exit code even if artifact publication succeeds. Coverage of a managed build is not proof of Native AOT runtime behavior.

TUnit documents custom output/log sinks and lifecycle/execution events, not a general public `ICustomReporter` API. Build a custom integration from `ILogSink`, event receivers, generated JSON/standard MTP extensions, or an actual documented engine API; do not invent a reporter interface.

## Extensibility

### Data and display extensions

- Typed custom data: derive `DataSourceGeneratorAttribute<T>` (or multi-type variants) and yield `Func<T>`/`Func<(...)>` from `GenerateDataSources(DataGeneratorMetadata)`.
- Async discovery data: derive `AsyncDataSourceGeneratorAttribute<T>` and return `IAsyncEnumerable<Func<Task<T>>>`. It still runs during discovery.
- Dynamic types: derive `UntypedDataSourceGeneratorAttribute` and yield factories of object arrays only when compile-time typing is unavailable.
- Lowest-level strongly typed async pipeline: `TypedDataSourceAttribute<T>.GetTypedDataRowsAsync`; prefer the generator bases unless full control is needed.
- Data generator instances can observe changing `dataGeneratorMetadata.TestBuilderContext.Current` after each `yield`; use the context/events instead of mutable attribute state for row-specific cleanup.
- `ArgumentDisplayFormatter` plus `[ArgumentDisplayFormatter<T>]` formats complex argument names. `DisplayNameFormatterAttribute.FormatDisplayName` controls full names.

```csharp
public sealed class PositiveCasesAttribute : DataSourceGeneratorAttribute<int>
{
    protected override IEnumerable<Func<int>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => 1;
        yield return () => 42;
    }
}

// Inside a test class:
[Test, PositiveCases]
public async Task AcceptsPositive(int value) =>
    await Assert.That(validator.Accepts(value)).IsTrue();
```

Use factories to avoid shared mutable instances; copy loop variables before closing over them. Do not use randomness without a recorded seed in discovery. Source-generated custom data attributes still have ordinary C# attribute-argument restrictions.

### Execution and lifecycle extensions

`ITestExecutor.ExecuteTest(TestContext, Func<ValueTask>)` wraps a body for transactions, STA dispatch, telemetry, or policy. Register with `[TestExecutor<T>]` at method/class/assembly or `TestRegisteredContext.SetTestExecutor`. Always invoke/await `action` exactly once unless the extension's explicit purpose says otherwise, preserve cancellation and exceptions, and put cleanup in `finally`.

For Windows COM/UI apartment requirements, the documented executor is `[TestExecutor<STAThreadExecutor>]`. A test-body executor and a hook executor are separate extension points; verify the thread requirements of setup/cleanup as well as the body.

`IHookExecutor` has one method for each before/after hook scope and receives hook metadata, context, and `Func<ValueTask> action`. Register with `[HookExecutor<T>]`; programmatic `SetHookExecutor` affects test-level hooks. Do not assume one generic method covers every hook.

`ITestRegisteredEventReceiver` can modify registration: executor, constraints, priority/limiter, skip/metadata through actual context methods. An explicit `[ParallelLimiter<T>]` wins over a programmatic limiter. Event receivers have `Order` where documented; a dynamically installed executor's own registration callback is not globally resorted.

`IParallelLimit` supplies `Limit`. `IParallelConstraint` is only a marker; actual scheduling semantics belong to built-in constraints or engine APIs, so do not invent a custom constraint protocol.

### Dynamic tests

For tests that cannot be expressed with a normal test/data source, create a public parameterless builder class with a public `[DynamicTestBuilder]` method taking `DynamicTestBuilderContext`, then add `DynamicTest<T>` instances. Its `TestMethod` is an expression using `DynamicTestHelper.Argument<T>()`; provide class/method/property arguments and attributes in the dynamic test model. Prefer ordinary source-generated tests for normal parameterization because they have simpler discovery and compile-time diagnostics.

```csharp
public sealed class DynamicExamples
{
    public async Task Check(int value) => await Assert.That(value).IsPositive();

    [DynamicTestBuilder]
    public void Build(DynamicTestBuilderContext context) =>
        context.AddTest(new DynamicTest<DynamicExamples>
        {
            TestMethod = test => test.Check(DynamicTestHelper.Argument<int>()),
            TestMethodArguments = [42]
        });
}
```

`TestMethod` is an expression describing the target, not a delegate that supplies argument values; the values come from `TestMethodArguments`. Runtime creation inside a test is also supported with `await TestContext.Current!.AddDynamicTest(new DynamicTest<T> { ... })`.

### Test infrastructure libraries

Reference `TUnit.Core` in a reusable infrastructure library. Consumers reference both that library and `TUnit`. Keep hooks/attributes narrow and version-compatible with consumer projects. Assertions and mocks can also be used independently of the TUnit runner.

## TUnit.Mocks

Use this section only when `TUnit.Mocks` is referenced or requested. It is a source-generated AOT-oriented mocking framework usable with any runner. At the snapshot it requires C# 14 (`TM004` otherwise); the recommended `T.Mock()` typed interface syntax uses C# 14 static extension members. `Mock.Of<T>()` is an alternate factory, not a workaround for the current package's language-version analyzer. The documentation's older-language fallback note conflicts with its current C# 14 requirement; follow the installed analyzer.

```csharp
var gateway = IPaymentGateway.Mock(MockBehavior.Strict);
gateway.Charge(Any<Money>()).Returns(new Receipt("r-1"));

IPaymentGateway sut = gateway; // Invoke through the interface, not the setup surface.
Receipt receipt = sut.Charge(new Money(10));
await Assert.That(receipt.Id).IsEqualTo("r-1");
gateway.Charge(Is<Money>(x => x.Amount == 10)).WasCalled(Times.Once);
```

Key rules:

- `T.Mock()` returns a generated wrapper; for interfaces it implements `T` directly and also exposes `.Object`. `Mock.Of<T>()`, `Mock.OfDelegate<T>()`, `Mock.Wrap(real)`, and `Mock.Of<T1,...,T4>()` cover alternate cases.
- Loose mode returns smart defaults/auto-mocks; strict throws for unconfigured calls. Set suite default at discovery through `context.Settings.Mocks.DefaultMode`, but explicit mode wins.
- Setup is a generated member call followed by `.Returns`, `.Throws`, `.Callback`, `.Then`, or `.ReturnsSequentially`. Async results are auto-wrapped. Without `.Then`, multiple return behaviors in one step apply together and the last return wins.
- Matchers include `Any`, exact raw/`Is`, inline predicate/`Is(predicate)`, null, regex, collection, range/set, `Not`, capture, params/ref-struct support. Matchers work for both setup and verification. `AnyArgs()` is available only on uniquely named methods.
- Generated property extensions support getter returns/throws/callback, `.Setter`/`.Set(...)`, and `SetupAllProperties()`. Out/ref setters are generated from parameter names (`SetsOutValue`, `SetsRefCount`).
- Partial class mocks call virtual base implementations when unconfigured. Wrap mocks delegate unconfigured calls to the real instance. Static abstract interface members require `[assembly: GenerateMock(typeof(T))]` and the generated `...Mockable` bridge.
- Verify with `.WasCalled(Times...)`, `.WasNeverCalled`, property verification, ordered verification, `VerifyAll`, `VerifyNoOtherCalls`, or inspect invocations. `MockRepository` groups behavior/reset/batch verification.
- Advanced APIs include events (raise/auto-raise/subscription tracking), state transitions, recursive auto-mocking, diagnostics, custom default value provider, and `Reset`. Experimental internals access publicizes compiler references through explicit MSBuild opt-in; prefer public seams and do not assume Native AOT support.
- `TUnit.Mocks.Http` provides `Mock.HttpClient`, `.Handler.OnGet/OnRequest(...).Respond...`, sequential responses, delays/errors, request capture/verification, and named `IHttpClientFactory` handlers. Unmatched requests default to 404 unless configured to throw.
- `TUnit.Mocks.Logging` provides `Mock.Logger<T>()`, captured `Entries`, fluent/shorthand `VerifyLog`, `VerifyNoLog(s)`, filters, and `Clear`.

Do not translate Moq/NSubstitute setup syntax literally. Read the generated TUnit.Mocks member API and compiler diagnostics. The setup/verification chain discriminator is significant.

## Integrations and other advanced features

Use official integration packages rather than rebuilding their behavior:

- `TUnit.AspNetCore`: `TestWebApplicationFactory<T>` and `WebApplicationTest<TFactory,TEntryPoint>` add per-test delegating factories over shared infrastructure, logging, automatic test-ID/activity propagation, and handler integration. Lifecycle: `ConfigureTestOptions` → `SetupAsync` → factory configuration → `ConfigureWebHostBuilder` → `ConfigureTestConfiguration` → `ConfigureTestServices` → application startup → body/disposal. Async setup can supply values for subsequent sync configuration. `Factory` is per test; `GlobalFactory` is shared. Use `ReplaceService<T>` in `ConfigureTestServices`, and isolated database/schema names. Enable `WebApplicationTestOptions.EnableHttpExchangeCapture` and inspect `HttpCapture.Last`/`Exchanges`; configure request/response body capture and size limits through `services.AddHttpExchangeCapture(...)`, not additional options on `WebApplicationTestOptions`. `CreateClientWithTestContext()` is obsolete; plain `CreateClient()`/`CreateDefaultClient()` now inject propagation. Analyzer `TUnit0064` warns about direct vanilla `WebApplicationFactory<T>` in this scenario.
- `TUnit.Aspire`: share `AspireFixture<Projects.AppHost>` with `PerTestSession`. Use `CreateHttpClient(resourceName)`, `GetConnectionStringAsync(name, ct)`, and `await using var logs = fixture.WatchResourceLogs(name)`. Override `ConfigureBuilder`, `ConfigureAppHost`, `Args`, `ResourceTimeout`, or `ResourcesToRemove` as needed. `ResourceWaitBehavior` is `AllHealthy` by default; `AllRunning`, `Named` with `ResourcesToWaitFor()`, or `None` are alternatives. Run migrations after `await base.InitializeAsync()`; preserve base disposal. The fixture handles resource readiness, failed-start diagnostics, logs, and OTLP correlation. Shared deployment does not isolate mutable application data.
- `TUnit.Playwright`: inherit `PageTest` (provides `Page`, `Context`, `Browser`, `Playwright`) or compose with `ClassDataSource<PageFixture>`. Configure launch options through the base constructor and override `BrowserName` for chromium/firefox/webkit. `[RecordVideo]` is method-level and automatically names/attaches recordings per test/attempt; it requires per-test `ContextFixture`/`PageFixture` (`SharedType.None`), though browsers may be shared. It refreshes contexts/pages before retry setup and finalizes recordings after teardown. `RecordVideoDir` alone records without automatic test attachments. Preserve base fixture initialization/disposal; use a parallel limiter for browser capacity.
- `TUnit.FsCheck`: `[Test, FsCheckProperty]` supports `bool`, `void`, `Task`/`ValueTask`, and FsCheck `Property` results. Configure `MaxTest`, `MaxFail`, `StartSize`, `EndSize`, `Replay`, `Verbose`, `QuietOnSuccess`, and custom `Arbitrary` types. Preserve the reported replay seed and shrunk counterexample; do not retry a falsified property. FsCheck integration is not Native AOT compatible. Its `bool` return is a specialized executor contract, not the normal test success mechanism.
- F# interactive has official examples. TUnit source generation is C# only; F#/VB use reflection mode, while `TUnit.Assertions.FSharp` supplies F# `taskAssert` syntax.
- .NET 10 file-based C# tests can start with `#:package TUnit@1.71.0`, declare ordinary test classes, and run via `dotnet run Tests.cs`; `dotnet project convert Tests.cs` converts to a project. Preserve exact `Directory.Build.props` casing on case-sensitive systems; do not assume arbitrary `.props` files are auto-imported. Global test-ID instrumentation, OpenTelemetry, complex nested infrastructure, and CI pipeline pages supply further patterns. Load the relevant current official page before implementing an integration because package-specific APIs change faster than core attributes.

`[Culture]`, generic test generation, deferred enumeration, isolation naming, explicit tests, test parameters, dynamic tests, report aggregation, and distributed tracing are easy to overlook; check them before writing custom infrastructure.

```csharp
// Requires TUnit.FsCheck and using TUnit.FsCheck;
[Test, FsCheckProperty(MaxTest = 200)]
public bool ReverseRoundTrips(int[] values) =>
    values.Reverse().Reverse().SequenceEqual(values);
```

## Migration from xUnit, NUnit, or MSTest

TUnit includes information-level migration analyzers/code fixes: `TUXU0001`, `TUNU0001`, and `TUMS0001`. A typical safe migration temporarily adds TUnit, disables TUnit implicit usings to avoid `Assert` ambiguity, builds, runs `dotnet format analyzers <project> --severity info --diagnostics <id>`, restores implicit-usings settings, performs manual conversions, removes old runner/Test SDK/Coverlet packages, then builds, lists, and runs tests. Preview with `--verify-no-changes`. Review every generated `TODO` and semantic change.

For multi-targeted projects, temporarily select one TFM in the project and restore the original targets after migration; this avoids the documented Roslyn linked-file fixer failure. Some migration pages suggest `dotnet format --framework`, but the checked .NET 10 SDK's formatter does not support that option. Consult the installed command's `--help` rather than copying it.

The migration docs use `<TUnitImplicitUsings>false</TUnitImplicitUsings>` and `<TUnitAssertionsImplicitUsings>false</TUnitAssertionsImplicitUsings>` during conversion. Check the installed package targets for accepted values before copying settings across versions. Do not run a migration analyzer across unrelated projects without selecting the intended project/solution.

Core mappings:

| Source concept | Idiomatic TUnit |
|---|---|
| xUnit `[Fact]`/`[Theory]`; MSTest `[TestMethod]`; NUnit `[Test]` | `[Test]`; remove `[TestClass]`/`[TestFixture]` |
| InlineData/DataRow/TestCase | `[Arguments]` |
| MemberData/DynamicData/TestCaseSource | typed static `[MethodDataSource]` |
| xUnit fixture/collection | `[ClassDataSource<T>]` with `PerClass` or keyed sharing |
| Setup/initialize | constructor or `[Before(Test)]`; static higher-scope hooks |
| Teardown/cleanup/IAsyncLifetime | `[After(Test)]`, `IAsyncDisposable`, managed fixture lifecycle |
| Trait/TestProperty | `[Property]`; categories use `[Category]` |
| classic assertions | awaited `Assert.That(actual)...`; verify expected/actual order |
| VSTest filter | MTP `--treenode-filter` |
| disabled parallel collection/fixture scope | explicit TUnit constraint/limiter only where needed |

Migration traps:

- NUnit usually reuses a class instance; TUnit never does. Move shared fixture state to a data source or explicit static/thread-safe owner.
- xUnit fixture constructors and MSTest context property injection do not map directly. Use class data/DI constructor extension and injected/current `TestContext`.
- Higher-scope TUnit hooks are static. Multiple cleanup hooks all run and aggregate failures.
- Assertions are awaited, fluent, and type-safe. Exception assertions should use current `await Assert.That(delegate).Throws...`, even if an older migration table shows a legacy direct `Assert.Throws` form.
- TUnit defaults to parallel for every invocation. A suite that was sequential must isolate resources or declare precise constraints.
- TUnit source data should use typed tuples/factories and static methods for AOT. Do not retain reflection-heavy object-array patterns by habit.
- Remove `Microsoft.NET.Test.Sdk` and Coverlet. Retain another framework package only while a deliberate mixed migration still needs it.

## Debugging guide

### No tests discovered

Check, in order:

1. Test project references `TUnit`, outputs an executable, and does not reference `Microsoft.NET.Test.Sdk`.
2. Method is public, instance, marked `[Test]`, non-`async void`, and source-generator diagnostics/build errors are resolved.
3. Run `dotnet build`, then `dotnet test --list-tests --log-level Debug` or `TUNIT_DISCOVERY_DIAGNOSTICS=1`; add `--minimum-expected-tests 1` in automated checks.
4. Verify IDE MTP support is enabled (Testing Platform server mode in Visual Studio; Testing Platform in Rider; C# Dev Kit setting in VS Code) and rebuild/cache-reset if needed.
5. For `InstanceMethodDataSource`, confirm discovery-time data is available; use `IAsyncDiscoveryInitializer` only if unavoidable.
6. Check source-generation versus reflection mode, language (C# vs F#/VB), AOT generic instantiations, and generated files/diagnostics. Use `--reflection` as a diagnostic or required compatibility mode, not a blanket fix.

### Data-source failure

Check parameter arity/types, tuple spreading, source accessibility/static requirement, factory freshness, cancellation and discovery timing. Turn on `TUNIT_DIAGNOSTIC_CAST=true` for conversion failures. Count matrix/combined Cartesian rows. An empty source creates no concrete rows. For deferred sources, enumeration errors appear at execution under the container rather than aborting assembly discovery.

### Assertion failure or false pass

Confirm every chain is awaited and the test returns `Task`. Verify the subject is actual and method argument is expected. Select equality versus equivalence/order/reference semantics deliberately. Avoid mixed `.And`/`.Or`. Use `Assert.Multiple` for independent facts and capture return values from narrowing assertions. When behavior seems version-sensitive, compile a minimal example against the installed package or inspect its XML/source API.

### Lifecycle failure

Map the failure to discovery, construction, property resolution, initialization, before hook, body, after hook, or disposal. Check static/instance hook rules and use the injected hook cancellation token. Remember execution initialization occurs after class hooks, discovery initialization occurs earlier, sibling property order is not a contract, and cleanup continues after earlier errors. Inspect aggregated exceptions rather than only the first line.

### Race/flaky failure

Re-run the focused test/class under normal parallelism, then with a precise limiter/key to prove resource contention. Inventory statics, shared fixtures, fixed files/ports/DB identifiers, environment/global culture, clocks/randomness, and fire-and-forget work. Use `context.Isolation`, deterministic time/signals, thread-safe shared resources, and awaited cleanup. A sequential pass does not prove correctness. Do not add retries until the transient external condition is identified.

### Hang/timeout/cancellation

Run with diagnostics and an outer `--timeout`; install MTP HangDump/CrashDump extensions when relevant. Remove sync-over-async, pass injected tokens to I/O, ensure producer/consumer tasks finish, and avoid discovery-time network/database work. `[Timeout]` cancels cooperatively; ignored tokens keep work alive until process-level forceful exit policy.

### Command/filter/report problem

Inspect `global.json` and `--help`; use MTP flags and the SDK-appropriate separator. Replace VSTest `--filter`. Confirm environment-variable precedence, results directory permissions, and reporter enable/disable variables. HTML aggregation requires both HTML and JSON sidecars. Use `--diagnostic`, `--diagnostic-verbosity`, `--log-level Debug`, and `--detailed-stacktrace` only as needed.

## Common AI mistakes

- Writing `[Fact]`, `[Theory]`, `[TestMethod]`, `[TestClass]`, `[TestFixture]`, or NUnit setup attributes in a TUnit test.
- Forgetting to await an assertion or creating `async void` tests/hooks.
- Treating one test-class instance as a fixture shared between methods.
- Assuming tests/classes/assemblies run sequentially; adding a broad `[NotInParallel]` instead of isolating or keying the one resource.
- Using `[ParallelGroup]` as a mutex. Its tests run concurrently inside a phase.
- Inventing standalone `[Order]` instead of `NotInParallel.Order`, or using order where `[DependsOn]`/one workflow test is required.
- Treating a `ClassDataSource` object as thread-safe merely because TUnit manages its lifetime.
- Returning the same mutable reference from a discovery data source rather than a factory.
- Performing slow external I/O during discovery or expecting `IAsyncInitializer` to have run for an instance source.
- Forgetting Cartesian explosion with matrix/combined data, or forgetting deferred rows cannot be independently filtered/dependencies.
- Injecting an unsourced `TestContext` parameter into a test method; use `TestContext.Current` (context parameters are supported for hooks).
- Injecting application services from `TestContext`, or claiming built-in container registrations/lifetimes that TUnit does not provide.
- Making class/assembly/session hooks instance methods, using the test cancellation token instead of the injected hook token, or blocking async cleanup.
- Assuming cancellation or timeout forcibly terminates ignored work.
- Retrying deterministic failures and hiding flaky tests.
- Mixing `.And` and `.Or`, assuming `IsEquivalentTo` preserves order, or copying obsolete `HasCount`/length shortcuts.
- Using VSTest `--filter`, `Microsoft.NET.Test.Sdk`, `.runsettings` assumptions, or Coverlet; omitting native MTP runner selection or using positional project paths with native MTP `dotnet test`.
- Reporting unsupported formats or inventing a custom reporter interface. Use actual HTML/JSON sidecar/TRX/JUnit/MTP APIs.
- Enabling reflection or disabling source generation as a generic discovery workaround.
- Copying documentation examples that conflict with current reference/source. Known current traps include stale `--filter`, mixed And/Or, older direct TestContext members, `--report-html`, legacy GitHub reporter env name, old ASP.NET client helper, and old count/length assertions.
- Introducing beta `TUnit.Assertions.Should`, C# 14 `TUnit.Mocks`, or experimental mocks internals access without checking project compatibility.

## Review and validation checklist

When editing or reviewing TUnit code, verify:

- `[Test]` methods and hooks have valid visibility/static/return signatures; every TUnit assertion is awaited.
- Data attributes match parameter types/count; sources use `nameof`, are static/AOT-safe where required, and create fresh mutable data.
- Discovery does not depend on execution initialization or slow external state; generic tests have explicit generated instantiations.
- Fixture sharing is intentional, lifecycle/disposal scope is correct, and shared state is immutable/thread-safe or precisely constrained.
- Tests remain independent under default parallel execution; names/ports/files/database data are isolated; no fire-and-forget work escapes.
- Dependencies are necessary, acyclic, and compatible with groups/limiters; failure propagation and data rows are handled.
- Timeouts/retries/cancellation forward tokens and preserve diagnostics. Retry conditions are narrow.
- Assertions use correct equality/order/reference/exception semantics, no mixed logical chain, and useful failure messages.
- Context/output/artifacts use current interfaces and unique results paths; background correlation is explicitly propagated.
- CLI/config/report choices match MTP, SDK, installed extensions, and precedence. No VSTest/Coverlet assumptions remain.
- Migration changes preserve fixture scope, setup/cleanup order, data-row meaning, and expected/actual semantics.

Validate proportionally:

1. `dotnet build` the affected test project so TUnit analyzers/source-generator diagnostics run.
2. `dotnet test --project <project> --list-tests` in native MTP mode when discovery/data source shape changed (use the legacy/direct syntax above for other modes).
3. Run the narrow tree filter for the affected method/class without rebuilding only if the build artifact is known current.
4. Run the whole affected project across target frameworks.
5. For parallel/shared-resource changes, repeat under normal/high concurrency and ensure cleanup leaves no state.
6. For runner/config/report changes, execute the exact CI-shaped command and inspect exit code plus expected result/artifact files.
7. For source-generation/AOT work, inspect generated code/diagnostics as needed and publish/run the intended Native AOT target when that is part of the requirement.

## Quick reference

```csharp
// Basic + data + timeout/cancellation
[Test]
[Arguments("alice", true)]
[Arguments("guest", false)]
[Category("Smoke")]
[Timeout(5_000)]
public async Task Authorizes(string user, bool expected, CancellationToken ct)
{
    var actual = await sut.AuthorizeAsync(user, ct);
    await Assert.That(actual).IsEqualTo(expected);
}

// Managed shared fixture
[ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
public sealed class ApiTests(AppFixture app)
{
    [Test, NotInParallel("users-table")]
    public async Task CreatesUser()
    {
        var response = await app.Client.PostAsync("/users", content: null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
    }

    // Dependency and retry only for a known transient
    [Test, DependsOn(nameof(CreatesUser)), NotInParallel("users-table")]
    [Retry(2, RetryOnExceptionTypes = [typeof(HttpRequestException)], BackoffMs = 100)]
    public Task ReadsUser() => ReadUserAsync();
}
```

Official entry points for version-sensitive follow-up: `https://tunit.dev/llms.txt`, `https://tunit.dev/docs/intro`, and `https://github.com/thomhurst/TUnit`. Prefer page Markdown (`.../page.md`) and official source. Fetch only the relevant page during an ordinary task; this skill already provides the cross-cutting model.

## Documentation coverage and maintenance

The research scope follows the official `llms.txt` inventory, repository `docs/sidebars.ts`, and linked pages outside the sidebar. In particular, programmatic configuration, HTML reporting, and the TestContext migration are included even where sidebar navigation omits them.

| Official documentation branch | Knowledge incorporated here |
|---|---|
| Intro; getting-started/* | Installation, project wiring, first tests, executable/MTP execution, IDEs |
| writing-tests/* | Every data-source family, row metadata, deferred enumeration, generic/AOT tests, properties/DI, lifecycle/hooks/events, context/artifacts, skip/explicit/culture, dependencies |
| assertions/* and assertions/extensibility/* | Awaiting, complete documented assertion families, composition/scopes, member/regex assertions, type checking, generated/manual/transforming assertions, F#, optional Should syntax |
| writing-tests/mocking/* | Factories, setup, matchers, verification, advanced mocking, HTTP and logging packages |
| execution/* | Filters, parameters, parallelism, cancellation/timeouts, repeat/retry, modes, CI reporting |
| reference/* | CLI, environment, programmatic settings, application JSON, hook-token diagnostic |
| extending/* | Executors/events, data generation, formatters/names, dynamic tests, log sinks, exceptions, coverage/MTP extensions, reusable libraries |
| examples/* | ASP.NET Core, Aspire, nested infrastructure, Playwright, FsCheck, F# interactive, file-based C#, test IDs, OpenTelemetry, CI pipelines |
| guides/* | Pitfalls, performance/philosophy, tracing, HTML and aggregated reports |
| comparison/*; migration/*; troubleshooting | Semantic migration, context reorganization, diagnostics and troubleshooting |
| benchmarks/* and generated benchmark pages | Benchmark scope/methodology and performance tradeoffs; avoid treating published timings as guarantees for a user's suite |

For maintenance, compare this inventory against the current official hierarchy; inspect the matching release's source for conflicting examples. The searchable assertions-library page embeds its API table in `docs/src/components/AssertionsLibrary/index.tsx`; inspect that table/source rather than treating an empty Markdown shell as the entire reference. SDK runner details are also grounded in Microsoft's official `unit-testing-with-dotnet-test` documentation.

Known corrections retained deliberately: `TestContext.Current` in test bodies; `NotInParallel.Order`; non-static generic provider types; named namespaces for generated assertion helpers; .NET 10 runner opt-in and `--project`; current mock language requirements and interface invocation; explicit JUnit enablement; HTML OS/TFM suffixes; current context interfaces, count/length chains, and ASP.NET client propagation.
