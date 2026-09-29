// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AterraEngine.Core.DependencyInjection.Generators;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
[Generator(LanguageNames.CSharp)]
public sealed class ActivatorGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("Aterra.ServiceAttributes.g.cs", SourceText.From(ServiceAttributeSource.Text, Encoding.UTF8)));

        IncrementalValuesProvider<(string Key, string Body, string Error)> models = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
            transform: static (syntaxContext, token) => ServiceModelFactory.DescribeDeclaration(syntaxContext, token)
        ).Where(static model => !string.IsNullOrEmpty(model.Key)).WithTrackingName("ServiceModels");

        context.RegisterSourceOutput(models.Collect(), ServiceRegistrationEmitter.Emit);
    }
}
