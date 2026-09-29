// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AterraEngine.Core.DependencyInjection.Generators.Tests;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------

public sealed class GeneratorEdgeTests {
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(ServiceLifetime).Assembly.Location).Distinct()
        .Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    private static CSharpCompilation Compile(string source, LanguageVersion languageVersion = LanguageVersion.Preview) =>
        CSharpCompilation.Create("GeneratorEdgeFixture",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(languageVersion))], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static GeneratorDriver Run(CSharpCompilation compilation, out Compilation updated) =>
        CSharpGeneratorDriver.Create([new ActivatorGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.Single().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out updated, out _);

    private static Task<ImmutableArray<Diagnostic>> AnalyzerDiagnostics(Compilation compilation) =>
        compilation.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

    [Test]
    public async Task MissingPublicConstructorIsDiagnosedAndNotGenerated() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            [TransientService<Service>]
            public sealed class Service { private Service() {} }
            """);

        // Act
        GeneratorDriver driver = Run(compilation, out Compilation updated);
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerDiagnostics(updated);

        // Assert
        await Assert.That(diagnostics).HasSingleItem();
        await Assert.That(diagnostics[0].GetMessage()).Contains("public constructor");
        await Assert.That(driver.GetRunResult().Results.Single().GeneratedSources.Any(
                source => source.HintName == "Aterra.GeneratedServiceRegistration.g.cs"))
            .IsFalse();
    }

    [Test]
    public async Task StaticFileLocalAndAbstractImplementationsAreDiagnosed() {
        // Arrange
        const string source = """
            using AterraEngine.Core.DependencyInjection;
            [TransientService<StaticService>] public static class StaticService {}
            [TransientService<FileService>] file sealed class FileService { public FileService() {} }
            [TransientService<AbstractService>] public abstract class AbstractService {}
            """;
        CSharpCompilation compilation = Compile(source);

        // Act
        Run(compilation, out Compilation updated);
        ImmutableArray<Diagnostic> diagnostics = await AnalyzerDiagnostics(updated);

        // Assert
        await Assert.That(diagnostics).Count().IsEqualTo(3);
        await Assert.That(diagnostics.All(diagnostic => diagnostic.Id == "ADI001")).IsTrue();
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.GetMessage()))
            .All(message => message.Contains("accessible") || message.Contains("concrete"));
    }

    [Test]
    public async Task InheritedRequiredMembersAreValidatedAndSetsRequiredMembersAllowsGeneration() {
        // Arrange
        CSharpCompilation invalid = Compile("""
            using AterraEngine.Core.DependencyInjection;
            public class Base { public required string Name { get; init; } }
            [TransientService<Service>] public sealed class Service : Base { public Service() {} }
            """);
        CSharpCompilation valid = Compile("""
            using System.Diagnostics.CodeAnalysis;
            using AterraEngine.Core.DependencyInjection;
            public class Base { public required string Name { get; init; } }
            [TransientService<Service>] public sealed class Service : Base {
                [SetsRequiredMembers] public Service() { Name = "set"; }
            }
            """);

        // Act
        Run(invalid, out Compilation invalidUpdated);
        GeneratorDriver validDriver = Run(valid, out Compilation validUpdated);

        // Assert
        await Assert.That((await AnalyzerDiagnostics(invalidUpdated)).Single().GetMessage())
            .Contains("required members");
        await Assert.That(await AnalyzerDiagnostics(validUpdated)).IsEmpty();
        await Assert.That(validUpdated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            .IsEmpty();
        await Assert.That(validDriver.GetRunResult().Results.Single().GeneratedSources.Any(
                source => source.HintName == "Aterra.GeneratedServiceRegistration.g.cs"))
            .IsTrue();
    }

    [Test]
    public async Task EveryServiceScopeValueEmitsTheExpectedLifetime() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            [Service<TransientService>(ServiceScope.Transient)] public sealed class TransientService {}
            [Service<SingletonService>(ServiceScope.Singleton)] public sealed class SingletonService {}
            [Service<HostService>(ServiceScope.Host)] public sealed class HostService {}
            [Service<WorldService>(ServiceScope.World)] public sealed class WorldService {}
            [Service<SceneService>(ServiceScope.Scene)] public sealed class SceneService {}
            """);

        // Act
        GeneratorDriver driver = Run(compilation, out Compilation updated);
        string source = driver.GetRunResult().Results.Single().GeneratedSources.Single(
            generated => generated.HintName == "Aterra.GeneratedServiceRegistration.g.cs").SourceText.ToString();

        // Assert
        await Assert.That(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            .IsEmpty();
        await Assert.That(source).Contains("ServiceLifetime.Transient");
        await Assert.That(source).Contains("ServiceLifetime.Singleton");
        await Assert.That(source).Contains("ServiceLifetime.Host");
        await Assert.That(source).Contains("ServiceLifetime.Of<global::AterraEngine.AterraWorld>()");
        await Assert.That(source).Contains("ServiceLifetime.Of<global::AterraEngine.AterraScene>()");
    }

    [Test]
    public async Task ConstructorDependencyTypesAreEmittedInBothResolverAndDependencyMetadata() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            public interface IDependency {}
            public sealed class Dependency : IDependency {}
            [TransientService<Service>]
            public sealed class Service(IDependency dependency, int count) {}
            """);

        // Act
        GeneratorDriver driver = Run(compilation, out Compilation updated);
        string source = driver.GetRunResult().Results.Single().GeneratedSources.Single(
            generated => generated.HintName == "Aterra.GeneratedServiceRegistration.g.cs").SourceText.ToString();

        // Assert
        await Assert.That(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            .IsEmpty();
        await Assert.That(source).Contains("resolver.Get<global::IDependency>()");
        await Assert.That(source).Contains("resolver.Get<int>()");
        await Assert.That(source).Contains("typeof(global::IDependency)");
        await Assert.That(source).Contains("typeof(int)");
    }

    [Test]
    public async Task UnrelatedAttributeAndUnattributedSourceProduceNoRegistration() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using System;
            [Obsolete] public sealed class Unrelated {}
            public sealed class Plain {}
            """);

        // Act
        GeneratorDriver driver = Run(compilation, out Compilation updated);
        GeneratorRunResult result = driver.GetRunResult().Results.Single();

        // Assert
        await Assert.That(result.GeneratedSources.Any(
                source => source.HintName == "Aterra.GeneratedServiceRegistration.g.cs"))
            .IsFalse();
        await Assert.That(await AnalyzerDiagnostics(updated)).IsEmpty();
        await Assert.That(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            .IsEmpty();
    }
}
