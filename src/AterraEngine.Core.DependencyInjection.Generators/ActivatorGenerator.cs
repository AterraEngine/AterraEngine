// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AterraEngine.Core.DependencyInjection.Generators;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
[Generator(LanguageNames.CSharp)]
public sealed class ActivatorGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        context.RegisterPostInitializationOutput(static output => {
            output.AddEmbeddedAttributeDefinition();
            output.AddSource("Aterra.ServiceAttributes.g.cs", SourceText.From(ServiceAttributeSource.Text, Encoding.UTF8));
        });

        IncrementalValueProvider<ImmutableArray<(string Key, string Body, string Error)>> serviceModels =
            FindServices(context, ServiceModelFactory.ServiceAttributeMetadataName, 0).Collect();
        IncrementalValueProvider<ImmutableArray<(string Key, string Body, string Error)>> singletonModels =
            FindServices(context, ServiceModelFactory.SingletonAttributeMetadataName, 1).Collect();
        IncrementalValueProvider<ImmutableArray<(string Key, string Body, string Error)>> hostModels =
            FindServices(context, ServiceModelFactory.HostAttributeMetadataName, 2).Collect();
        IncrementalValueProvider<ImmutableArray<(string Key, string Body, string Error)>> worldModels =
            FindServices(context, ServiceModelFactory.WorldAttributeMetadataName, 3).Collect();
        IncrementalValueProvider<ImmutableArray<(string Key, string Body, string Error)>> sceneModels =
            FindServices(context, ServiceModelFactory.SceneAttributeMetadataName, 4).Collect();
        IncrementalValueProvider<ImmutableArray<(string Key, string Body, string Error)>> transientModels =
            FindServices(context, ServiceModelFactory.TransientAttributeMetadataName, 5).Collect();
        IncrementalValueProvider<ImmutableArray<(string Key, string Body, string Error)>> scopedModels =
            FindServices(context, ServiceModelFactory.ScopedAttributeMetadataName, 6).Collect();

        IncrementalValueProvider<string> source = serviceModels
            .Combine(singletonModels)
            .Combine(hostModels)
            .Combine(worldModels)
            .Combine(sceneModels)
            .Combine(transientModels)
            .Combine(scopedModels)
            .Select(static (models, token) => ServiceRegistrationEmitter.Render(
            [
                .. models.Left.Left.Left.Left.Left.Left,
                .. models.Left.Left.Left.Left.Left.Right,
                .. models.Left.Left.Left.Left.Right,
                .. models.Left.Left.Left.Right,
                .. models.Left.Left.Right,
                .. models.Left.Right,
                .. models.Right
            ], token))
            .WithTrackingName("RegistrationSource");

        context.RegisterSourceOutput(source, ServiceRegistrationEmitter.Emit);
    }

    private static IncrementalValuesProvider<(string Key, string Body, string Error)> FindServices(
        IncrementalGeneratorInitializationContext context,
        string metadataName,
        int markerOrder
    ) => context.SyntaxProvider.ForAttributeWithMetadataName(
            metadataName,
            predicate: static (_, _) => true,
            transform: (attributeContext, token) => ServiceModelFactory.DescribeAttributedType(attributeContext, markerOrder, token))
        .Where(static model => !string.IsNullOrEmpty(model.Key))
        .WithTrackingName("ServiceModels");
}
