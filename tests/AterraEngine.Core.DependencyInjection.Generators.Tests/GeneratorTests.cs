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
public class GeneratorTests {
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(ServiceLifetime).Assembly.Location).Distinct()
        .Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    private static CSharpCompilation Compile(
        string source,
        LanguageVersion languageVersion = LanguageVersion.Preview,
        bool warningsAsErrors = false
    ) => CSharpCompilation.Create("GeneratorFixture",
        [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(languageVersion))], References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            generalDiagnosticOption: warningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default));

    private static GeneratorDriver Driver(CSharpParseOptions? parseOptions = null) => CSharpGeneratorDriver.Create(
        [new ActivatorGenerator().AsSourceGenerator()], parseOptions: parseOptions ?? new CSharpParseOptions(LanguageVersion.Preview),
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

    [Test]
    public async Task ServiceAttributesGenerateRegistrationsAndActivators() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            using AterraEngine.Core.DependencyInjection.Scopes;
            namespace Game;
            public interface IClock {}
            public interface ISettings {}
            public interface ICache {}
            public sealed class Scope {}
            [HostService<IClock>]
            public sealed class Clock : IClock {}
            [SingletonService<ISettings>]
            public sealed class Settings : ISettings {}
            [Service<ICache>(ServiceScope.Singleton)]
            public sealed class Cache : ICache {}
            [Service<Worker>(ServiceScope.Transient)]
            public sealed class Worker(IClock clock) { public IClock Clock { get; } = clock; }
            [ScopedService<ScopedWorker, Scope>]
            public sealed class ScopedWorker(Worker worker) { public Worker Worker { get; } = worker; }
            [WorldService<WorldWorker>]
            public sealed class WorldWorker {}
            [SceneService<SceneWorker>]
            public sealed class SceneWorker {}
            [Service<GeneralWorld>(ServiceScope.World)]
            public sealed class GeneralWorld {}
            [Service<GeneralScene>(ServiceScope.Scene)]
            public sealed class GeneralScene {}
            """);

        // Act
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        string source = RegistrationSource(driver);

        // Assert
        await Assert.That(source.Contains("AddGeneratedActivator<global::Game.Clock>")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.IClock, global::Game.Clock>(global::AterraEngine.Core.DependencyInjection.ServiceLifetime.Host)")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.ISettings, global::Game.Settings>(global::AterraEngine.Core.DependencyInjection.ServiceLifetime.Singleton)")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.ICache, global::Game.Cache>(global::AterraEngine.Core.DependencyInjection.ServiceLifetime.Singleton)")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.Worker, global::Game.Worker>(global::AterraEngine.Core.DependencyInjection.ServiceLifetime.Transient)")).IsTrue();
        await Assert.That(source.Contains("ServiceLifetime.Of<global::Game.Scope>()")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.WorldWorker, global::Game.WorldWorker>(global::AterraEngine.Core.DependencyInjection.ServiceLifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.World>())")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.SceneWorker, global::Game.SceneWorker>(global::AterraEngine.Core.DependencyInjection.ServiceLifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.Scene>())")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.GeneralWorld, global::Game.GeneralWorld>(global::AterraEngine.Core.DependencyInjection.ServiceLifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.World>())")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.GeneralScene, global::Game.GeneralScene>(global::AterraEngine.Core.DependencyInjection.ServiceLifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.Scene>())")).IsTrue();
        await Assert.That(source.Contains("GeneratedServiceRegistration.RegisterAssembly")).IsTrue();
        await Assert.That(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
        await Assert.That(await updated.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync()).IsEmpty();
    }

    [Test]
    public async Task MarkedConstructorIsUsedWhenSeveralPublicConstructorsExist() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            [TransientService<Service>]
            public sealed class Service {
                public Service() {}
                [ServiceConstructor]
                public Service(int seed) {}
            }
            """);

        // Act
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        string source = RegistrationSource(driver);

        // Assert
        await Assert.That(source.Contains("new global::Service(resolver.Get<int>())")).IsTrue();
        await Assert.That(source.Contains("typeof(int)")).IsTrue();
        await Assert.That(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
    }

    [Test]
    [Arguments("public abstract class Service {}", "concrete class")]
    [Arguments("public class Service { private Service() {} }", "public constructor")]
    [Arguments("public class Service { public Service() {} public Service(int value) {} }", "ServiceConstructor")]
    [Arguments("public class Service { public Service(ref int value) {} }", "unsupported")]
    [Arguments("public class Service { public Service(dynamic value) {} }", "dynamic")]
    [Arguments("public class Service { public required string Name { get; init; } }", "required members")]
    [Arguments("public class Service<T> {}", "non-generic")]
    public async Task InvalidImplementationsProduceActionableDiagnostics(string declaration, string message) {
        // Arrange
        CSharpCompilation compilation = Compile($$"""
            using AterraEngine.Core.DependencyInjection;
            [TransientService<Service>]
            {{declaration}}
            """);

        // Act
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        ImmutableArray<Diagnostic> diagnostics = await updated.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("ADI001");
        await Assert.That(diagnostics[0].GetMessage().Contains(message)).IsTrue();
        await Assert.That(diagnostics[0].Location.IsInSource).IsTrue();
        await Assert.That(driver.GetRunResult().Results.Single().GeneratedSources.Any(source =>
            source.HintName == "Aterra.GeneratedServiceRegistration.g.cs")).IsFalse();
    }

    [Test]
    public async Task MoreThanOneMarkedConstructorIsDiagnosed() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            [TransientService<Service>]
            public sealed class Service {
                [ServiceConstructor] public Service() {}
                [ServiceConstructor] public Service(int value) {}
            }
            """);

        // Act
        Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        ImmutableArray<Diagnostic> diagnostics = await updated.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics.Single().GetMessage().Contains("more than one")).IsTrue();
    }

    [Test]
    public async Task NonPublicMarkedConstructorIsDiagnosed() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            [TransientService<Service>]
            public sealed class Service {
                public Service() {}
                [ServiceConstructor] private Service(int value) {}
            }
            """);

        // Act
        Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        ImmutableArray<Diagnostic> diagnostics = await updated.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics.Single().GetMessage().Contains("must be public")).IsTrue();
    }

    [Test]
    public async Task IncompatibleAndDuplicateServiceDeclarationsAreDiagnosed() {
        // Arrange
        CSharpCompilation incompatible = Compile("""
            using AterraEngine.Core.DependencyInjection;
            public interface IService {}
            [HostService<IService>]
            public sealed class Service {}
            """);
        CSharpCompilation duplicate = Compile("""
            using AterraEngine.Core.DependencyInjection;
            [HostService<Service>, TransientService<Service>]
            public sealed class Service {}
            """);

        // Act
        Driver().RunGeneratorsAndUpdateCompilation(incompatible, out Compilation updatedIncompatible, out _);
        Driver().RunGeneratorsAndUpdateCompilation(duplicate, out Compilation updatedDuplicate, out _);
        ImmutableArray<Diagnostic> incompatibleDiagnostics = await updatedIncompatible.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        ImmutableArray<Diagnostic> duplicateDiagnostics = await updatedDuplicate.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(incompatibleDiagnostics.Single().GetMessage().Contains("not assignable")).IsTrue();
        await Assert.That(duplicateDiagnostics.Single().GetMessage().Contains("more than once")).IsTrue();
    }

    [Test]
    public async Task AttributesAcrossPartialDeclarationsProduceOneActivator() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            public interface IFirst {}
            public interface ISecond {}
            [HostService<IFirst>]
            public sealed partial class Service : IFirst, ISecond {}
            [TransientService<ISecond>]
            public sealed partial class Service {}
            """);

        // Act
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        string source = RegistrationSource(driver);

        // Assert
        await Assert.That(source.Split("AddGeneratedActivator<global::Service>").Length - 1).IsEqualTo(1);
        await Assert.That(source.Contains("Add<global::IFirst, global::Service>")).IsTrue();
        await Assert.That(source.Contains("Add<global::ISecond, global::Service>")).IsTrue();
        await Assert.That(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
    }

    [Test]
    public async Task UnrelatedEditReusesServiceModels() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            [TransientService<Service>]
            public sealed class Service {}
            """);
        GeneratorDriver driver = Driver().RunGenerators(compilation);

        // Act
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public class Unrelated {}", (CSharpParseOptions)compilation.SyntaxTrees.Single().Options));
        driver = driver.RunGenerators(compilation);
        ImmutableArray<IncrementalGeneratorRunStep> steps = driver.GetRunResult().Results.Single().TrackedSteps["ServiceModels"];

        // Assert
        await Assert.That(steps.SelectMany(step => step.Outputs).All(output =>
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue();
    }

    [Test]
    public async Task UnrelatedAttributedTypesDoNotEnterServicePipeline() {
        // Arrange
        CSharpCompilation compilation = Compile("[System.Obsolete] public sealed class Unrelated {}");

        // Act
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        GeneratorRunResult result = driver.GetRunResult().Results.Single();

        // Assert
        await Assert.That(result.GeneratedSources.Any(source =>
            source.HintName == "Aterra.GeneratedServiceRegistration.g.cs")).IsFalse();
        await Assert.That(!result.TrackedSteps.TryGetValue("ServiceModels", out ImmutableArray<IncrementalGeneratorRunStep> steps) ||
            steps.SelectMany(step => step.Outputs).Any() is false).IsTrue();
        await Assert.That(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
    }

    [Test]
    public async Task RegistrationOutputIsIndependentOfDeclarationOrder() {
        // Arrange
        const string firstOrder = """
            using AterraEngine.Core.DependencyInjection;
            namespace Game;
            public interface IAlpha {}
            public interface IZulu {}
            [TransientService<Zulu>] public sealed class Zulu {}
            [TransientService<IZulu>, SingletonService<IAlpha>]
            public sealed class Alpha : IAlpha, IZulu {}
            """;
        const string secondOrder = """
            using AterraEngine.Core.DependencyInjection;
            namespace Game;
            public interface IZulu {}
            public interface IAlpha {}
            [SingletonService<IAlpha>, TransientService<IZulu>]
            public sealed class Alpha : IZulu, IAlpha {}
            [TransientService<Zulu>] public sealed class Zulu {}
            """;

        // Act
        string first = RegistrationSource(Driver().RunGenerators(Compile(firstOrder)));
        string second = RegistrationSource(Driver().RunGenerators(Compile(secondOrder)));

        // Assert
        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.IndexOf("global::Game.Alpha", StringComparison.Ordinal))
            .IsLessThan(first.IndexOf("global::Game.Zulu", StringComparison.Ordinal));
    }

    [Test]
    public async Task InvalidServiceScopeValueProducesDiagnostic() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            using AterraEngine.Core.DependencyInjection.Scopes;
            [Service<Service>((ServiceScope)999)]
            public sealed class Service {}
            """);

        // Act
        Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        ImmutableArray<Diagnostic> diagnostics = await updated.WithAnalyzers([new ActivatorDeclarationAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics).HasSingleItem();
        await Assert.That(diagnostics[0].Id).IsEqualTo("ADI001");
        await Assert.That(diagnostics[0].GetMessage().Contains("invalid ServiceScope value", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GeneratedContractCompilesWithCSharp11AndWarningsAsErrors() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            [TransientService<Service>]
            public sealed class Service {}
            """, LanguageVersion.CSharp11, warningsAsErrors: true);
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.Single().Options;

        // Act
        GeneratorDriver driver = Driver(parseOptions).RunGeneratorsAndUpdateCompilation(
            compilation, out Compilation updated, out ImmutableArray<Diagnostic> generatorDiagnostics);

        // Assert
        await Assert.That(driver.GetRunResult().Results.Single().Exception).IsNull();
        await Assert.That(generatorDiagnostics).IsEmpty();
        await Assert.That(updated.GetDiagnostics()).IsEmpty();
    }

    [Test]
    public async Task EditingOneServiceReusesTheOtherServiceModel() {
        // Arrange
        const string original = """
            using AterraEngine.Core.DependencyInjection;
            public sealed class FirstDependency {}
            public sealed class SecondDependency {}
            [TransientService<FirstService>] public sealed class FirstService(FirstDependency dependency) {}
            [TransientService<SecondService>] public sealed class SecondService {}
            """;
        CSharpCompilation compilation = Compile(original);
        GeneratorDriver driver = Driver().RunGenerators(compilation);
        SyntaxTree originalTree = compilation.SyntaxTrees.Single();
        SyntaxTree changedTree = CSharpSyntaxTree.ParseText(
            original.Replace("FirstService(FirstDependency", "FirstService(SecondDependency", StringComparison.Ordinal),
            (CSharpParseOptions)originalTree.Options);

        // Act
        compilation = compilation.ReplaceSyntaxTree(originalTree, changedTree);
        driver = driver.RunGenerators(compilation);
        ImmutableArray<(object Value, IncrementalStepRunReason Reason)> outputs = driver.GetRunResult().Results.Single()
            .TrackedSteps["ServiceModels"].SelectMany(step => step.Outputs).ToImmutableArray();

        // Assert
        await Assert.That(outputs.Count(output => output.Reason == IncrementalStepRunReason.Modified)).IsEqualTo(1);
        await Assert.That(outputs.Count(output =>
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task AliasedAndFullyQualifiedAttributesAreDiscoveredSemantically() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using Transient = AterraEngine.Core.DependencyInjection.TransientServiceAttribute<Service>;
            [Transient]
            public sealed class Service {}
            [global::AterraEngine.Core.DependencyInjection.SingletonServiceAttribute<OtherService>]
            public sealed class OtherService {}
            """);

        // Act
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        string source = RegistrationSource(driver);

        // Assert
        await Assert.That(source.Contains("Add<global::Service, global::Service>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("Add<global::OtherService, global::OtherService>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
    }

    private static string RegistrationSource(GeneratorDriver driver) => driver.GetRunResult().Results.Single().GeneratedSources
        .Single(source => source.HintName == "Aterra.GeneratedServiceRegistration.g.cs").SourceText.ToString();
}
