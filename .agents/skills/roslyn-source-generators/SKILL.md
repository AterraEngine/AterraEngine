---
name: roslyn-source-generators
description: Design, implement, review, debug, test, optimize, package, or migrate C# Roslyn source generators. Prefer IIncrementalGenerator and cache-friendly attribute-driven pipelines; use for generator code, generated-source contracts, AdditionalFiles/configuration, diagnostics, incremental behavior, and analyzer packaging. Do not use for ordinary C# analyzers that produce no source or for IL weaving/code rewriting.
---

# Roslyn Source Generators

Use this skill to produce source generators that are additive, deterministic, responsive in the IDE, and compatible with their intended Roslyn hosts. It synthesizes the official Roslyn source-generator cookbook, its incremental replacement, and the incremental-generator design documentation. It is self-contained; consult upstream sources only when an exact API is version-sensitive or experimental.

## Authority labels

Interpret statements using these labels:

- **[Required]** is behavior or an API constraint enforced by Roslyn, C#, or MSBuild.
- **[Roslyn]** is an explicit recommendation in the official Roslyn cookbook/design docs.
- **[Practice]** is engineering guidance inferred from those constraints and reliable generator design.
- **[Experimental]** is current Roslyn functionality without a stable compatibility promise.

If the repository has stricter conventions or a lower supported Roslyn version, follow those constraints. Verify APIs against the lowest `Microsoft.CodeAnalysis` version the package promises to support.

## First decisions

Before editing code, identify:

1. The generator's explicit opt-in signal: preferably a sealed marker attribute.
2. The smallest semantic facts needed to render each output.
3. Whether output is naturally one file per input or must aggregate all inputs.
4. Which inputs genuinely affect output: syntax, compilation facts, additional files, parse options, analyzer config, or metadata references.
5. The minimum supported Roslyn/compiler host and target framework.
6. Invalid user states and whether a generator diagnostic or separate analyzer should own each one.

Use this routing guide:

| Need | Preferred pattern |
|---|---|
| Find attributed declarations | `SyntaxProvider.ForAttributeWithMetadataName` |
| Find syntax with no viable attribute contract | Narrow `CreateSyntaxProvider` predicate, then semantic transform |
| Emit a fixed marker attribute/helper | `RegisterPostInitializationOutput` |
| Transform declared non-C# inputs | `AdditionalTextsProvider` |
| Use project or per-file settings | `AnalyzerConfigOptionsProvider` plus compiler-visible MSBuild properties/metadata |
| Use one compilation fact | Project that fact from `CompilationProvider` immediately, then combine it |
| Emit independently for each model | Register directly on `IncrementalValuesProvider<T>` |
| Emit one aggregate registry | Normalize, sort, `Collect`, then emit once |
| Validate source usage with precise code locations/fixes | Separate `DiagnosticAnalyzer` and optional code fix |
| Report malformed additional input or generation-only failure | Equatable error model followed by `ReportDiagnostic` |
| Rewrite source, inject call sites, weave IL | Source generators are the wrong tool |

## Architecture and terminology

- **[Required] Additive only.** A generator adds C# syntax trees. It cannot modify/delete user syntax, rewrite calls, optimize IL, or inject statements into existing bodies. Augment user code through partial types or partial members.
- **[Required] Standard generators are unordered.** They receive the same input compilation and do not consume ordinary output from other generators. Do not create ordering dependencies.
- **[Required] A generated type can be referenced by user source in the same compilation.** User code may be written as though it already exists.
- **[Required] Generator assemblies are loaded as analyzers.** `[Generator(LanguageNames.CSharp)]` marks the implementation. An assembly may contain generators and diagnostic analyzers.
- **[Required] Hosts control generator lifetime.** Store no mutable per-compilation state in fields or statics. `Initialize` declares an immutable pipeline; deferred callbacks do the work.
- **[Required] Generated source participates in normal C# binding and diagnostics.** Emit valid code for the consumer's language version and references.
- **[Roslyn] Implement `IIncrementalGenerator`.** The official classic cookbook marks `ISourceGenerator` deprecated in favor of the incremental API.

An incremental pipeline is a directed graph of providers and transforms. `IncrementalValueProvider<T>` carries one value. `IncrementalValuesProvider<T>` carries zero or more independently tracked values. `Initialize` builds the graph; the host evaluates it later.

```csharp
[Generator(LanguageNames.CSharp)]
public sealed class WidgetGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<WidgetModel> models = /* pipeline */;
        context.RegisterSourceOutput(models, static (spc, model) =>
            spc.AddSource(model.HintName, Render(model)));
    }
}
```

Keep lambdas `static` where possible. This prevents accidental capture of generator instances or mutable state and exposes dependencies.

## Classic `ISourceGenerator`

Use classic generators only to maintain an existing implementation or support a host that cannot run incremental generators.

```csharp
[Generator(LanguageNames.CSharp)]
public sealed class LegacyGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context) =>
        context.RegisterForSyntaxNotifications(static () => new Receiver());

    public void Execute(GeneratorExecutionContext context)
    {
        // Read Compilation, AdditionalFiles, AnalyzerConfigOptions;
        // call AddSource and/or ReportDiagnostic.
    }
}
```

- **[Required]** The classic methods use `GeneratorInitializationContext` and `GeneratorExecutionContext`. Pre-release names (`SourceGeneratorContext`, `IntializationContext`, `RunFullGeneration`) are obsolete.
- **[Roslyn]** Replace `ISyntaxReceiver`/`ISyntaxContextReceiver` scanning with incremental syntax providers. Receivers visit broad syntax and have no fine-grained cached model.
- **[Practice]** Migrate by separating discovery, semantic projection, validation, and rendering, then express those stages as providers. Do not place a monolithic `Execute` body inside one incremental callback.
- **[Required]** Tests create drivers with `CSharpGeneratorDriver.Create(...)`, not obsolete constructors, and retain the returned immutable driver from every run.

## Build an incremental pipeline

### Provider operations

- `Select`: map each input to one output. Project large Roslyn objects into small values.
- `Where`: keep or discard each item. Filter as early as correctness permits.
- `SelectMany`: map one item to zero or many independently tracked items. Use when one file/declaration defines several generated units.
- `Collect`: convert many values into one `ImmutableArray<T>`. Any item change can invalidate the aggregate; use only when output truly depends on the whole set.
- `Combine`: pair single providers, or every item in a values provider with one single value. There is no direct many-to-many combine; collect one side only if a cross-set dependency is required.
- `WithComparer`: override equality at one edge. Use only when the comparer accurately represents semantic sameness.
- Reuse a provider in several branches to split a pipeline. Each branch caches independently.

Extract the only compilation fact that affects output before combining it:

```csharp
IncrementalValueProvider<string?> assemblyName = context.CompilationProvider
    .Select(static (compilation, _) => compilation.AssemblyName);

IncrementalValuesProvider<InputFile> files = context.AdditionalTextsProvider
    .Where(static file => file.Path.EndsWith(".widget", StringComparison.OrdinalIgnoreCase))
    .Select(static (file, ct) => Parse(file.Path, file.GetText(ct)));

IncrementalValuesProvider<(InputFile Left, string? Right)> inputs =
    files.Combine(assemblyName);
```

Do not combine every file with the whole `Compilation` merely to read `AssemblyName`; every source edit changes the compilation object and needlessly invalidates downstream work.

### Cache-friendly model design

- **[Roslyn] Extract information early.** Symbols, compilations, semantic models, syntax nodes, locations, `AdditionalText`, and option providers are inputs to inspect, not final models.
- **[Roslyn] Never store `ISymbol` in a pipeline model.** Symbols lack useful cross-run value equality and can retain old compilations.
- **[Roslyn] Remove `SyntaxNode` and `Location` quickly.** Any edit in their tree generally replaces them. Keep a node only across the shortest stage that needs its tree, such as reading tree-specific options.
- **[Roslyn] Use value-equatable models.** Records, readonly record structs, strings, enums, primitives, `TextSpan`, and explicit equatable wrappers are suitable.
- **[Required] Collection equality must match content.** Arrays, `List<T>`, and `ImmutableArray<T>` ordinarily compare by identity. A record containing one is not content-equatable. Use a tested immutable wrapper/comparer or flatten small fixed data.
- **[Practice] Normalize at the semantic boundary.** Store fully qualified emitted type names, escaped identifiers, normalized options, and sorted immutable data.
- **[Practice] Split expensive work into meaningful transforms.** Each value-equatable result is a cache checkpoint. Avoid stages that merely copy the same non-equatable object.

```csharp
internal sealed record MethodModel(
    string Namespace,
    string ContainingType,
    string MethodName,
    string ReturnType,
    string HintName);
```

If a custom comparer says unequal values are equal, Roslyn may reuse stale output. A comparer is a correctness boundary, not just a performance switch.

### Granularity and aggregation

Prefer one model and stable output per independent declaration:

```csharp
context.RegisterSourceOutput(models, static (spc, model) =>
    spc.AddSource(model.HintName, Render(model)));
```

Use `Collect` for registries, dispatch tables, or collision resolution that depends on all inputs:

```csharp
IncrementalValueProvider<ImmutableArray<ServiceModel>> allServices = models.Collect();

IncrementalValueProvider<RegistryModel> registry = allServices.Select(static (items, _) =>
    BuildRegistry(items.OrderBy(static x => x.MetadataName, StringComparer.Ordinal)));

context.RegisterSourceOutput(registry, static (spc, model) =>
    spc.AddSource("MyProduct.ServiceRegistry.g.cs", RenderRegistry(model)));
```

Sort aggregate inputs with explicit ordinal comparers. Provider enumeration order is not a public semantic contract.

### Cancellation

- **[Roslyn]** Forward tokens to Roslyn calls such as `GetText`, `GetDeclaredSymbol`, and `GetSyntax`.
- Call `ThrowIfCancellationRequested` within expensive loops.
- Let cancellation unwind. Do not save partial results in mutable state.

## Syntax and semantic analysis

### Attribute-driven discovery

**[Roslyn] Prefer `ForAttributeWithMetadataName` whenever an attribute can express opt-in.** Official guidance reports it is at least 99 times more efficient than a general syntax provider in common cases because Roslyn indexes attribute names before realizing syntax and semantics.

```csharp
private const string MarkerName = "Acme.GenerateWidgetAttribute";

IncrementalValuesProvider<WidgetModel> widgets = context.SyntaxProvider
    .ForAttributeWithMetadataName(
        fullyQualifiedMetadataName: MarkerName,
        predicate: static (node, _) => node is TypeDeclarationSyntax,
        transform: static (attributeContext, ct) =>
        {
            var type = (INamedTypeSymbol)attributeContext.TargetSymbol;
            AttributeData marker = attributeContext.Attributes[0];
            return WidgetModel.Create(type, marker, ct);
        });
```

- Pass the fully qualified **metadata name**, without `global::` or assembly name. A generic attribute uses backtick arity, such as ``Acme.MarkerAttribute`1``.
- The predicate is syntactic and cheap. It receives a node and token, not a semantic model.
- The transform receives `GeneratorAttributeSyntaxContext`: `TargetNode`, `TargetSymbol`, `SemanticModel`, and matching `Attributes`.
- Read constructor/named arguments from `AttributeData`; do not parse attribute text.
- `TypedConstant` can represent arrays, enums, `typeof` values, null, and errors. Validate `Kind`, `IsNull`, `Value`, and `Values` before casting.
- Seal generated marker attributes and set explicit `AttributeUsage`, `Inherited`, and `AllowMultiple`.

Do not discover indirect marker relationships by scanning all types:

- **[Roslyn]** Do not scan for indirectly implemented interfaces, indirect base types, attributes on bases/interfaces, or attributes derived from an unsealed marker. These require compilation-wide traversal and cannot be usefully incremental.
- Prefer a direct marker on each target. Use an analyzer to enforce a required base/interface.
- Attribute inheritance is also a poor configuration mechanism: generators do not instantiate attributes, so base-constructor/property runtime behavior is unavailable.

### General syntax providers

Use `CreateSyntaxProvider` only when no reasonable attribute contract exists:

```csharp
IncrementalValuesProvider<CandidateModel?> candidates = context.SyntaxProvider
    .CreateSyntaxProvider(
        predicate: static (node, _) =>
            node is MethodDeclarationSyntax { Modifiers.Count: > 0 },
        transform: static (syntaxContext, ct) =>
        {
            var syntax = (MethodDeclarationSyntax)syntaxContext.Node;
            var symbol = syntaxContext.SemanticModel.GetDeclaredSymbol(syntax, ct);
            return symbol is null ? null : CandidateModel.From(symbol);
        });

IncrementalValuesProvider<CandidateModel> valid = candidates
    .Where(static model => model is not null)
    .Select(static (model, _) => model!);
```

- Make the predicate purely syntactic, allocation-light, and selective. It may run over many nodes.
- Expect semantic transforms to rerun after edits in other files; remote edits can change binding. The projected value model stops further work.
- Do not use `node.ToString()` as semantic identity. Resolve symbols and extract only facts needed by output.

### Working safely with symbols

- Use `SymbolEqualityComparer.Default` inside transforms. Do not compare display strings to decide identity.
- Use `Compilation.GetTypeByMetadataName` for known types and handle `null` for missing/ambiguous results.
- Emit type references with `SymbolDisplayFormat.FullyQualifiedFormat` or a deliberate custom format. `global::` avoids consumer using/alias collisions.
- Escape declaration identifiers. Prefix regular/contextual keywords with `@`; use `SyntaxFacts.GetKeywordKind` and `GetContextualKeywordKind`.
- Account for global/named namespaces, nested types, generic arity/parameters/constraints, nullability, tuples, arrays, pointer/function-pointer/ref kinds when supported, accessibility, and containing structure.
- Use symbol APIs rather than syntax text for aliases, qualified attributes, partial declarations, and inferred types.
- A symbol may have several `DeclaringSyntaxReferences`; define whether output is per symbol, declaration, or attribute application.

### Partial augmentation

To add members to a user type:

- **[Required]** The target and every containing type that must be reopened must be `partial` and kind-compatible. A `file` type cannot be augmented from another generated file.
- Reproduce namespace and containing-type nesting, type parameters, and required constraints.
- Check generated-member collisions and report a diagnostic or let a separate analyzer prevent them.
- Decide how records, structs, readonly/ref-like/static types, primary constructors, and generic nesting affect generated declarations.
- Prefer partial methods when the user should control a signature and the generator supplies the implementation.

### Pattern cards

Use these designs as starting points, then apply the pipeline/equality rules above.

**Generate a standalone class or member set**

- **What/when:** Add an API that user source can call when its shape derives from declared inputs.
- **How:** Project those inputs to a declaration model and emit a stable file. Use post-init only when the text is completely fixed.
- **Why:** This matches the additive model and lets normal C# binding validate the API.
- **Mistakes:** Generating the same full type from several inputs, unstable hint names, or assuming another ordinary generator can consume it.

**Augment a partial type**

- **What/when:** Add properties, methods, events, or interface declarations to an opted-in user type. The cookbook's `INotifyPropertyChanged` scenario uses attributed backing fields and generates properties/events instead of rewriting existing properties.
- **How:** Discover direct markers, build one semantic model per logical target, validate the entire containing partial chain, then reopen that exact type.
- **Why:** Partial declarations are C#'s supported additive composition mechanism.
- **Mistakes:** Losing namespace/nesting/generic constraints, adding a duplicate event/member for several fields, or trying to augment a file-local/non-partial target.

**Implement explicitly named interfaces**

- **What/when:** An attribute takes `typeof(IMyContract)` arguments and generated partial declarations implement the requested contracts.
- **How:** Read `TypedConstant` type arguments as `INamedTypeSymbol`, validate `TypeKind.Interface`, flatten the specifically named interfaces' required members, resolve member/signature conflicts, and project all signatures to equatable text/value models.
- **Why:** Work is bounded by explicit user input; no compilation-wide derived-type scan is needed.
- **Mistakes:** Scanning all types for indirect implementations, omitting inherited interface members, generating illegal setters/accessibility, or ignoring default/static/ambiguous members and generic substitutions.

**Compile-time serialization or mapping**

- **What/when:** Replace repeated runtime reflection with code specialized from compile-time type information.
- **How:** Mark participating types, inspect property/field symbols, model the complete supported contract, then render escaped deterministic code.
- **Why:** Moves known work to compilation and can reduce runtime reflection/cost.
- **Mistakes:** The classic cookbook's illustrative `p.Type.ToString() == "int"` and hand-built JSON are not production techniques. Use symbols, define null/escaping/culture/cycle/accessibility behavior, and diagnose unsupported shapes.

**Transform an additional file**

- **What/when:** A declared schema/config/protocol file is the source of C# declarations.
- **How:** Filter `AdditionalTextsProvider`, parse each file to an equatable model, report located parse errors, and emit per file/record unless an aggregate is necessary.
- **Why:** Roslyn tracks declared additional inputs and can invalidate only affected work.
- **Mistakes:** Hidden file I/O, basename collisions, ignoring null text, collecting before parsing, or treating malformed explicit input as absent.

## Fixed generated declarations

Use post-initialization output for source that depends on no user/project input, especially marker attributes:

```csharp
context.RegisterPostInitializationOutput(static postInit =>
{
    postInit.AddEmbeddedAttributeDefinition(); // Roslyn 4.14+
    postInit.AddSource("Acme.GenerateWidgetAttribute.g.cs", """
        // <auto-generated/>
        #nullable enable
        using System;
        using Microsoft.CodeAnalysis;

        namespace Acme;

        [Embedded]
        [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
        internal sealed class GenerateWidgetAttribute : Attribute
        {
        }
        """);
});
```

- **[Required]** Post-initialization takes no provider input. Its output is available to later steps in that generator's compilation view.
- **[Roslyn]** Apply `Microsoft.CodeAnalysis.EmbeddedAttribute` to generated internal marker types and call `AddEmbeddedAttributeDefinition`. This avoids lookup warnings when projects expose identical internal markers through `InternalsVisibleTo`.
- **[Roslyn]** `AddEmbeddedAttributeDefinition` requires Roslyn 4.14+. For older hosts, ship marker attributes in a referenced contract assembly or a verified compatibility design.
- If the product already ships an abstractions assembly, defining public marker attributes there may be clearer than generating them.

## Generated source and `AddSource`

`RegisterSourceOutput` and `RegisterImplementationSourceOutput` end a pipeline. Their `SourceProductionContext` can call `AddSource` and `ReportDiagnostic`.

```csharp
private static void Emit(SourceProductionContext context, WidgetModel model)
{
    context.CancellationToken.ThrowIfCancellationRequested();
    context.AddSource(model.HintName, SourceText.From(Render(model), Encoding.UTF8));
}
```

- **[Required]** Hint names must be unique within one generator run and stable for the logical output. Duplicate hint names are a design bug.
- **[Practice]** Use `Product.<sanitized-metadata-name>.<stable-hash>.g.cs`. Include namespace, containing types, arity, and overload identity as needed. Do not rely on simple type/file basenames.
- Make collisions deterministic. Derive a stable hash from complete identity with specified encoding/algorithm; never use process-dependent `string.GetHashCode()`.
- Include `// <auto-generated/>` and explicit nullable context. Fully qualify type references where practical.
- Generate only required `using`s. Consumer global usings are not a reliable dependency.
- Render with invariant culture, correct escaping, stable line endings/order. Never embed timestamps, random data, machine paths, current directory, ambient environment, network results, or undeclared files.
- Avoid leaking absolute paths unless needed to locate an additional-file error.
- **[Roslyn] Prefer an indented text writer over constructing `SyntaxNode`s.** `AddSource` accepts text, `NormalizeWhitespace` is expensive, and syntax construction is awkward for templates. Small raw/interpolated strings are suitable.
- Parse output in tests when useful; do not build syntax merely to call `NormalizeWhitespace` in production.

Use `RegisterImplementationSourceOutput` only when output has no semantic effect for IDE analysis. A host may omit it during analysis, though executable builds run it. Use `RegisterSourceOutput` for declarations user code binds to.

## Diagnostics and failure behavior

Never silently skip input that clearly expresses generator intent but violates its contract.

| Error | Preferred owner |
|---|---|
| Invalid attributed declaration, missing `partial`, member collision, unsupported source shape | Separate `DiagnosticAnalyzer`; offer a code fix when useful |
| Malformed/conflicting `AdditionalFiles` input | Generator pipeline diagnostic |
| Missing runtime reference required by emitted code | Analyzer or generator diagnostic with setup instructions |
| Internal generator exception | Fix the generator; do not turn every exception into a vague diagnostic |

- **[Required]** `SourceProductionContext.ReportDiagnostic` supports generator diagnostics.
- **[Roslyn]** The incremental cookbook recommends a separate analyzer for source-code problems. Precise source locations and semantic validation often push non-equatable syntax/location state through the generator; analyzers fit this and can offer fixes.
- **[Practice]** For generator-owned diagnostics, output an equatable `GenerationResult<Model, ErrorInfo>`. Construct the `Diagnostic` only in the terminal callback. For additional files, store path and value-based spans, then call `Location.Create(path, textSpan, lineSpan)`.
- Use stable IDs, static descriptors, clear severities, actionable messages, and the offending attribute/token/file span where feasible.
- Distinguish no opt-in input (emit nothing) from invalid opt-in input (give feedback).
- Catch expected parse/validation exceptions near the boundary. Never convert `OperationCanceledException` to a diagnostic.

```csharp
private static readonly DiagnosticDescriptor InvalidSpec = new(
    id: "ACMEGEN001",
    title: "Invalid widget specification",
    messageFormat: "Widget specification '{0}' is invalid: {1}",
    category: "Acme.Generation",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

context.RegisterSourceOutput(results, static (spc, result) =>
{
    if (result.Error is { } error)
    {
        var location = Location.Create(error.Path, error.Span, error.LineSpan);
        spc.ReportDiagnostic(Diagnostic.Create(InvalidSpec, location, error.Name, error.Message));
        return;
    }

    spc.AddSource(result.Model!.HintName, Render(result.Model));
});
```

## Additional files

Consumers declare non-C# inputs with MSBuild:

```xml
<ItemGroup>
  <AdditionalFiles Include="Specs/**/*.widget" />
</ItemGroup>
```

Process per file before collecting:

```csharp
IncrementalValuesProvider<SpecResult> specs = context.AdditionalTextsProvider
    .Where(static file => file.Path.EndsWith(".widget", StringComparison.OrdinalIgnoreCase))
    .Select(static (file, ct) =>
    {
        SourceText? text = file.GetText(ct);
        return text is null
            ? SpecResult.Unreadable(file.Path)
            : ParseSpec(file.Path, text, ct);
    });
```

- Handle `GetText` returning `null`.
- Choose a deliberate path comparison policy. Extension checks are often ordinal-ignore-case; identity may need normalized separators and a repository-relative name.
- Different directories can contain the same basename. Include enough identity in hint names.
- Do not perform arbitrary file I/O for hidden dependencies. Require affecting files as `AdditionalFiles` so the host tracks them.
- Split multi-record files with `SelectMany` only when records can be modeled independently and have stable identities.

## Analyzer configuration and MSBuild

### Analyzer config options

`AnalyzerConfigOptionsProvider` exposes global and per-`SyntaxTree`/`AdditionalText` options. Values are strings and need explicit parsing.

```csharp
IncrementalValueProvider<bool> emitLogging = context.AnalyzerConfigOptionsProvider
    .Select(static (provider, _) =>
        provider.GlobalOptions.TryGetValue("acme_generator.emit_logging", out string? value) &&
        bool.TryParse(value, out bool enabled) && enabled);
```

- Define defaults and invalid-value behavior. Diagnose nontrivial invalid settings rather than silently choosing surprising values.
- Parse enums/numbers/booleans invariantly.
- For tree-specific options, briefly combine the node/tree with options, read the value, then project away the node.
- Use `.globalconfig` for global keys. `.editorconfig` can scope values where file/tree context is meaningful.

### Compiler-visible MSBuild values

MSBuild exposes only properties/metadata explicitly made compiler-visible:

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="AcmeGenerator_Mode" />
  <CompilerVisibleItemMetadata Include="AdditionalFiles"
                               MetadataName="AcmeGenerator_Mode" />
</ItemGroup>
```

Read them as:

```csharp
provider.GlobalOptions.TryGetValue("build_property.AcmeGenerator_Mode", out var mode);
provider.GetOptions(file).TryGetValue(
    "build_metadata.AdditionalFiles.AcmeGenerator_Mode", out var perFileMode);
```

- Ship these declarations in package `.props`/`.targets` when consumers should not add them.
- Apply per-file metadata over a global default with a clear precedence rule.
- **[Required behavior]** Values travel through analyzer-config text. Nontrivial values can lose data; semicolon is an editorconfig comment delimiter. Use documented transport encoding or redesign the setting, then decode deterministically.
- Prefer feature detection through compilation symbols over inferring API availability from `TargetFramework`.

## Packaging and compatibility

### Project references during development

```xml
<ProjectReference Include="../Acme.Generator/Acme.Generator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

A generator project commonly uses:

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>
  <IsRoslynComponent>true</IsRoslynComponent>
  <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp"
                    Version="LOWEST_SUPPORTED_VERSION"
                    PrivateAssets="all" />
</ItemGroup>
```

`netstandard2.0` remains a broad-compatibility choice for many hosts; it is not a universal Roslyn API requirement. Use a newer target only if every supported host loads it.

### NuGet layout

- **[Required for automatic analyzer discovery]** Put the generator under `analyzers/dotnet/cs`.
- Set `IncludeBuildOutput=false` when it should not also be a normal library reference.
- Put consumer build integration in `build`/`buildTransitive` according to the package contract.
- Inspect the final `.nupkg`; do not assume the project reference graph creates the correct layout.

```xml
<PropertyGroup>
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  <IncludeBuildOutput>false</IncludeBuildOutput>
</PropertyGroup>
<ItemGroup>
  <None Include="$(OutputPath)$(AssemblyName).dll"
        Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
</ItemGroup>
```

### Dependencies

1. A **consumer/runtime dependency** is referenced by emitted source. Ship it normally or require it explicitly, then validate needed symbols in the consumer compilation.
2. A **generator-time dependency** loads into the compiler process. Mark it `PrivateAssets="all"` and package required assemblies beside the generator in `analyzers/dotnet/cs`. Minimize these dependencies to reduce load-context/version conflicts.

### Roslyn versioning

- Build against the lowest supported Roslyn API. A generator built against newer assemblies can fail in an older host (`CS8032` or missing members).
- Keep Roslyn package references private; use the host compiler's assemblies.
- Test the minimum supported SDK/compiler and a current one. New APIs such as `AddEmbeddedAttributeDefinition` need an explicit minimum.
- Generated syntax must match consumer `CSharpParseOptions.LanguageVersion`. Prefer the promised minimum syntax or branch from a small projected parse-option model and diagnose unsupported modes.
- Feature-detect framework APIs from symbols. TFMs/package versions are only proxies for the compiled surface.

## Experimental pre-compilation generation

**[Experimental]** Current Roslyn main documents `RegisterPreCompilationSourceOutput`, guarded by `RSEXPERIMENTAL007`.

It emits source from non-compilation inputs before the standard compilation-dependent phase. Later standard pipelines can see those declarations through `CompilationProvider`.

- It may use additional files, parse options, and analyzer config inputs.
- Connecting it to `CompilationProvider` or `SyntaxProvider` throws at runtime.
- `PreCompilationSourceProductionContext` has no `ReportDiagnostic`; use an analyzer/standard path.
- Use it only when generated non-compilation data must become semantic input. Fixed markers belong in post-init; ordinary code belongs in source output.
- Do not base a stable product on it without pinning/testing exact Roslyn versions and accepting compatibility risk.

## Testing

Test the generator as a compiler component, not only its renderer.

### Driver harness

```csharp
IIncrementalGenerator generator = new WidgetGenerator();
GeneratorDriver driver = CSharpGeneratorDriver.Create(
    generators: new[] { generator.AsSourceGenerator() },
    parseOptions: (CSharpParseOptions)input.SyntaxTrees.First().Options);

driver = driver.RunGeneratorsAndUpdateCompilation(
    input, out Compilation output, out ImmutableArray<Diagnostic> driverDiagnostics);

GeneratorRunResult result = driver.GetRunResult().Results.Single();
Assert.Null(result.Exception);
Assert.Empty(driverDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
```

- **[Required]** `GeneratorDriver` is immutable. Assign results from `RunGenerators`, `RunGeneratorsAndUpdateCompilation`, `ReplaceAdditionalTexts`, and related methods.
- Use `.AsSourceGenerator()` where an API accepts `ISourceGenerator`.
- Make parse/compilation options, references, additional texts, and analyzer options representative.
- Assert run exceptions, generator diagnostics, hint names/text, and updated compilation diagnostics.
- Prefer semantic behavior assertions plus a few exact golden-source tests. Substring-only tests can miss invalid code.
- `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` is an official option for classic generators. For incremental generators use a compatible adapter or direct driver; the current incremental cookbook leaves its testing-library example as a TODO.

### Required test matrix

- no matching input, one valid input, several inputs, deterministic ordering;
- invalid opt-in declarations and malformed additional files;
- fully qualified, aliased, and short attribute spellings;
- duplicate attributes, partial declarations, attributes split across partial declarations;
- global namespace, nesting, generics, records/structs, escaped identifiers, nullability;
- same simple name in different namespaces and same additional-file basename in different directories;
- missing required APIs/references and different language versions;
- global/per-file configuration, invalid/missing values;
- diagnostic ID, severity, message, and location;
- output compilation, including warnings-as-errors if the product promises warning-free output.

### Test incrementality

Enable tracking only in tests:

```csharp
var options = new GeneratorDriverOptions(
    disabledOutputs: IncrementalGeneratorOutputKind.None,
    trackIncrementalGeneratorSteps: true);

GeneratorDriver driver = CSharpGeneratorDriver.Create(
    generators: new[] { new WidgetGenerator().AsSourceGenerator() },
    driverOptions: options);
```

Name important providers with `WithTrackingName("WidgetModels")`. Run once, retain the returned driver, change one input, and run the same driver again. Inspect `TrackedSteps` and `IncrementalStepRunReason`.

Assert that unrelated edits leave projected models `Cached`/`Unchanged`; one opted-in edit affects only its output where possible; global settings invalidate only dependent branches; one additional-file edit does not reparse others; semantic changes rerun affected transforms. Tracking has overhead and is not a production option.

## Debugging and investigation

1. Reproduce through a small driver test first; inspect generated sources, diagnostics, tracked steps, and `GeneratorRunResult.Exception`.
2. Materialize output during builds:

   ```xml
   <PropertyGroup>
     <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
     <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)Generated</CompilerGeneratedFilesOutputPath>
   </PropertyGroup>
   ```

3. For build-only failures, create a binary log (`dotnet build -bl`) and inspect analyzer loading, additional files, config, and compiler invocation.
4. Debug the unit-test process where possible. Otherwise attach to the compiler/server process or temporarily use `Debugger.Launch()` behind a private debug-only guard; never ship unconditional launch/wait code.
5. For stale output, verify the datum is a provider input and equality does not hide it, then inspect tracked reasons.
6. For load failures, check host Roslyn version, TFM, analyzer package layout, and generator-time dependencies.

Do not depend on console output from compiler-hosted generators. Use tests, debugger inspection, binary logs, or intentional diagnostics.

## Performance and correctness rules

### Determinism

- **[Required for correct caching]** A transform's result is a pure function of declared provider inputs. Roslyn may reuse cached results without invoking it.
- Read no clock, random source, network, ambient environment, undeclared file, mutable singleton, or process-specific hash.
- Use invariant formatting and explicit ordinal sorting.
- Keep hint names/text stable when semantic input is unchanged.
- Never assume execution order or standard cross-generator visibility.

### Avoid broad invalidation

- Prefer FAWMN; filter syntax/files before expensive work.
- Project tiny facts from compilation, parse options, and config before combining.
- Avoid `Collect` without a real all-items dependency; emit per item where possible.
- Avoid models with symbols, nodes, locations, or reference-equality collections.
- Use direct marker attributes rather than inheritance/interface scans.
- Forward cancellation and bound expensive parsing/generation.

### Generated-code safety

- Fully qualify type references and escape identifiers/literals.
- Match namespace, nesting, generic signature, and partial constraints.
- Check/diagnose member and type collisions.
- Handle empty/global namespace.
- Do not expose generator implementation types in consumer APIs.
- If output needs runtime libraries, validate exact symbols and emit setup guidance.

## Common failure modes

| Symptom | Cause | Correction |
|---|---|---|
| Whole generator reruns on each edit | Raw compilation/symbol/node or bad collection equality reaches late stages | Project equatable facts early |
| Every output regenerates for one edit | Premature `Collect` or monolithic output | Emit from a values provider |
| Stale output | Incorrect comparer, hidden state/I/O, incomplete model | Restore truthful equality and inputs |
| Duplicate hint name | Simple type/file basename | Include full identity and stable hash |
| Attribute works with one spelling | Textual syntax matching | Use FAWMN and `AttributeData` |
| Duplicate partial-type generation | Pipeline is per syntax/attribute but contract is per symbol | Enforce one marker or group by stable identity |
| Partial does not merge | Missing `partial`, wrong namespace/nesting/kind/arity, file-local target | Validate full shape and diagnose |
| Works in tests, not build | Newer Roslyn API, unsupported TFM, missing dependency | Test minimum host and package layout |
| File changes ignored | Direct file I/O | Declare/consume `AdditionalFiles` |
| MSBuild option missing | Not compiler-visible or wrong key/context | Add visibility items; use correct prefix |
| Option truncated | Analyzer-config special characters | Encode or redesign value |
| Only CS8785 appears | Unhandled generator exception | Validate, handle expected errors, assert `RunResult.Exception` |
| Output differs across machines | Culture/path/newline/order/time/random | Normalize and render deterministically |
| IDE slow despite fast build | Broad scans or semantic transforms | Direct attributes, narrow predicates, equatable checkpoints |

## Implementation workflow

1. Inspect Roslyn/package versions, project TFM, analyzer rules, language versions, and tests.
2. Define opt-in and generated-code contracts; confirm the task is additive.
3. Choose only providers that can affect output.
4. Use the cheapest discovery filter, normally FAWMN.
5. Resolve semantics and immediately project a value-equatable model.
6. Validate shapes; route source diagnostics to an analyzer where practical and generator-input errors through an equatable result.
7. Split independent output; collect only genuine aggregates and sort them.
8. Render escaped, fully qualified source with stable hint names.
9. Test positive, negative, compilation, collision, configuration, partial/nested/generic cases.
10. Add an incremental rerun test for a nontrivial pipeline.
11. Test the minimum host and inspect packed analyzer layout if distributed.

## Review checklist

### Architecture and pipeline

- `IIncrementalGenerator` unless legacy support is explicit?
- Additive design, declared inputs, no mutable state/order assumption?
- FAWMN where possible; cheap early filters and narrow semantic transforms?
- Symbols/nodes/locations/compilation removed early?
- Truthful value equality including nested collections?
- `Collect` necessary and `Combine` inputs reduced first?
- Cancellation forwarded?

### Semantics and output

- Semantic symbol comparisons and fully qualified emitted names?
- Partial declarations, nesting, generics, global namespace, and collisions handled?
- Stable unique hint names; identifiers/literals escaped; deterministic text?
- Output compiles under supported languages/references?

### User experience and distribution

- Invalid explicit input gets actionable feedback?
- Source-shape diagnostics use an analyzer where appropriate?
- Config defaults/precedence documented and tested?
- Generated attributes embedded or shipped in a contract assembly?
- Generator under `analyzers/dotnet/cs`, Roslyn private, dependencies packaged?
- Tests assert exceptions, diagnostics, text, output compilation, and cache reuse?

## Source provenance and intentional modernization

Official sources reviewed in full:

- Roslyn `docs/features/source-generators.cookbook.md` (classic cookbook; current main deprecates `ISourceGenerator`).
- Roslyn `docs/features/incremental-generators.cookbook.md` (modern cookbook).
- Roslyn `docs/features/incremental-generators.md` (providers, caching, cancellation, outputs).
- Roslyn `docs/features/source-generators.md` (additive/host architecture).

Intentional adaptations:

- Classic receivers/monolithic `Execute` examples remain only as migration context; current examples use incremental providers.
- Raw syntax-text inspection is replaced by semantic symbol/attribute analysis.
- The old serializer sample is treated as a use case, not correct production JSON.
- Driver tests use `Create`, immutable reassignment, and `.AsSourceGenerator()`.
- Diagnostics follow current guidance: prefer an analyzer for source usage while retaining supported generator diagnostics for generator-owned inputs.
- `RegisterPreCompilationSourceOutput` and `AddEmbeddedAttributeDefinition` carry their experimental/minimum-version constraints.
- Historical package versions are not recommendations; choose versions from the promised host compatibility range.
