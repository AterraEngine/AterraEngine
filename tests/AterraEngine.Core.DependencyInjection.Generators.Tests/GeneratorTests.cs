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
        .Append(typeof(Lifetime).Assembly.Location).Distinct()
        .Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    private static CSharpCompilation Compile(string source) => CSharpCompilation.Create("GeneratorFixture",
        [CSharpSyntaxTree.ParseText(source)], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ActivatorGenerator().AsSourceGenerator()],
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

    [Test]
    public async Task ServiceAttributesGenerateRegistrationsAndActivators() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            namespace Game;
            public interface IClock {}
            public interface ISettings {}
            public interface ICache {}
            public sealed class Scope {}
            [HostService<IClock>]
            public sealed class Clock : IClock {}
            [SingletonService<ISettings>]
            public sealed class Settings : ISettings {}
            [Service<ICache>(ServiceLifetime.Singleton)]
            public sealed class Cache : ICache {}
            [Service<Worker>(ServiceLifetime.Transient)]
            public sealed class Worker(IClock clock) { public IClock Clock { get; } = clock; }
            [ScopedService<ScopedWorker, Scope>]
            public sealed class ScopedWorker(Worker worker) { public Worker Worker { get; } = worker; }
            [WorldService<WorldWorker>]
            public sealed class WorldWorker {}
            [SceneService<SceneWorker>]
            public sealed class SceneWorker {}
            [Service<GeneralWorld>(ServiceLifetime.World)]
            public sealed class GeneralWorld {}
            [Service<GeneralScene>(ServiceLifetime.Scene)]
            public sealed class GeneralScene {}
            """);

        // Act
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        string source = RegistrationSource(driver);

        // Assert
        await Assert.That(source.Contains("AddGeneratedActivator<global::Game.Clock>")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.IClock, global::Game.Clock>(global::AterraEngine.Core.DependencyInjection.Lifetime.Host)")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.ISettings, global::Game.Settings>(global::AterraEngine.Core.DependencyInjection.Lifetime.Singleton)")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.ICache, global::Game.Cache>(global::AterraEngine.Core.DependencyInjection.Lifetime.Singleton)")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.Worker, global::Game.Worker>(global::AterraEngine.Core.DependencyInjection.Lifetime.Transient)")).IsTrue();
        await Assert.That(source.Contains("Lifetime.Of<global::Game.Scope>()")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.WorldWorker, global::Game.WorldWorker>(global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.World>())")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.SceneWorker, global::Game.SceneWorker>(global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.Scene>())")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.GeneralWorld, global::Game.GeneralWorld>(global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.World>())")).IsTrue();
        await Assert.That(source.Contains("Add<global::Game.GeneralScene, global::Game.GeneralScene>(global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.Scene>())")).IsTrue();
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
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Unrelated {}"));
        driver = driver.RunGenerators(compilation);
        ImmutableArray<IncrementalGeneratorRunStep> steps = driver.GetRunResult().Results.Single().TrackedSteps["ServiceModels"];

        // Assert
        await Assert.That(steps.SelectMany(step => step.Outputs).All(output =>
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue();
    }

    private static string RegistrationSource(GeneratorDriver driver) => driver.GetRunResult().Results.Single().GeneratedSources
        .Single(source => source.HintName == "Aterra.GeneratedServiceRegistration.g.cs").SourceText.ToString();
}
