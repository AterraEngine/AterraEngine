using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AterraEngine.Core.DependencyInjection.Generators.Tests;
public class GeneratorTests {
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(GenerateServiceActivatorsAttribute).Assembly.Location).Distinct()
        .Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    private static CSharpCompilation Compile(string source) => CSharpCompilation.Create("GeneratorFixture",
        [CSharpSyntaxTree.ParseText(source)], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ActivatorGenerator().AsSourceGenerator()],
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));

    [Test]
    public async Task GeneratedConstructorsAndDependencyMetadataCompile() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            namespace Game;
            public interface IClock {}
            public sealed class Generic<T> { public Generic(IClock clock, int seed) {} }
            public class Outer { public sealed class Nested { public Nested() {} } }
            [GenerateServiceActivators(typeof(Generic<string>), typeof(Outer.Nested))]
            public static partial class Services {}
            """);

        // Act
        GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);
        string source = driver.GetRunResult().Results.Single().GeneratedSources.Single().SourceText.ToString();

        // Assert
        await Assert.That(source.Contains("AddGeneratedActivator<global::Game.Generic<string>>")).IsTrue();
        await Assert.That(source.Contains("ref global::AterraEngine.Core.DependencyInjection.GeneratedServiceResolver resolver")).IsTrue();
        await Assert.That(source.Contains("new global::Game.Generic<string>(resolver.Get<global::Game.IClock>(), resolver.Get<int>())")).IsTrue();
        await Assert.That(source.Contains("typeof(global::Game.IClock), typeof(int)")).IsTrue();
        await Assert.That(updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
        await Assert.That(await updated.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync()).IsEmpty();
    }

    [Test]
    [Arguments("public class Service { public Service() {} public Service(int value) {} }", "exactly one public constructor")]
    [Arguments("public abstract class Service {}", "concrete class")]
    [Arguments("public class Service { private Service() {} }", "exactly one public constructor")]
    [Arguments("public class Service { public Service(ref int value) {} }", "unsupported")]
    [Arguments("public class Service { public Service(dynamic value) {} }", "dynamic")]
    [Arguments("file class Service {}", "concrete class")]
    [Arguments("public class Service { public required string Name { get; init; } }", "required members")]
    public async Task InvalidConstructorsProduceActionableDiagnostics(string declaration, string message) {
        // Arrange
        CSharpCompilation compilation = Compile($$"""
            using AterraEngine.Core.DependencyInjection;
            {{declaration}}
            [GenerateServiceActivators(typeof(Service))]
            public static partial class Services {}
            """);

        // Act
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        GeneratorDriver driver = Driver().RunGenerators(compilation);

        // Assert
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("ADI001");
        await Assert.That(diagnostics[0].GetMessage().Contains(message)).IsTrue();
        await Assert.That(diagnostics[0].Location.IsInSource).IsTrue();
        await Assert.That(driver.GetRunResult().Results.Single().GeneratedSources).IsEmpty();
    }

    [Test]
    [Arguments("public class Services {}", "static partial")]
    [Arguments("public partial record Services;", "static partial")]
    [Arguments("public static partial class Services<T> {}", "non-generic")]
    [Arguments("public static partial class Services { public static void AddActivators() {} }", "reserved")]
    public async Task InvalidModulesAreDiagnosed(string declaration, string message) {
        // Arrange
        CSharpCompilation compilation = Compile($$"""
            using AterraEngine.Core.DependencyInjection;
            [GenerateServiceActivators()]
            {{declaration}}
            """);

        // Act
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers([new ActivatorDeclarationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics.Single().GetMessage().Contains(message)).IsTrue();
    }

    [Test]
    public async Task GlobalNamespaceAndEscapedNamesCompile() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            public class @event {}
            [GenerateServiceActivators(typeof(@event))]
            public static partial class @class {}
            """);

        // Act
        Driver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        // Assert
        await Assert.That(updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray()).IsEmpty();
    }

    [Test]
    public async Task UnrelatedEditReusesIncrementalOutput() {
        // Arrange
        CSharpCompilation compilation = Compile("""
            using AterraEngine.Core.DependencyInjection;
            public class Service {}
            [GenerateServiceActivators(typeof(Service))]
            public static partial class Services {}
            """);
        GeneratorDriver driver = Driver().RunGenerators(compilation);

        // Act
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Unrelated {}"));
        driver = driver.RunGenerators(compilation);
        ImmutableArray<IncrementalGeneratorRunStep> steps = driver.GetRunResult().Results.Single().TrackedSteps["ActivatorModels"];

        // Assert
        await Assert.That(steps.SelectMany(step => step.Outputs).All(output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue();
    }

    [Test]
    public async Task ConstructorEditInvalidatesGeneratedRecipe() {
        // Arrange
        const string original = """
            using AterraEngine.Core.DependencyInjection;
            public class Service { public Service() {} }
            [GenerateServiceActivators(typeof(Service))]
            public static partial class Services {}
            """;
        CSharpCompilation compilation = Compile(original);
        GeneratorDriver driver = Driver().RunGenerators(compilation);

        // Act
        compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(original.Replace("Service()", "Service(int seed)")));
        driver = driver.RunGenerators(compilation);
        string source = driver.GetRunResult().Results.Single().GeneratedSources.Single().SourceText.ToString();

        // Assert
        await Assert.That(source.Contains("resolver.Get<int>()")).IsTrue();
    }
}
