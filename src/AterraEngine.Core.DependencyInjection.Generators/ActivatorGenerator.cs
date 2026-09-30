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
        // The marker attributes must exist before normal source generation so user code can bind to them in the same
        // compilation. Marking them as embedded prevents conflicts when internals are exposed to another assembly.
        context.RegisterPostInitializationOutput(static output => {
            output.AddEmbeddedAttributeDefinition();
            output.AddSource("Aterra.ServiceAttributes.g.cs", SourceText.From(ServiceAttributeSource.Text, Encoding.UTF8));
        });

        // Roslyn indexes these exact metadata names. Separate indexed pipelines avoid semantically inspecting every
        // attributed type in the compilation, including types carrying unrelated framework or test attributes.
        // The numeric order elects one pipeline to describe a type that uses several different service attributes.
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
        IncrementalValueProvider<ImmutableArray<(string Key, string Body, string Error)>> closureModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(ServiceModelFactory.ClosureAttributeMetadataName, predicate: static (_, _) => true,
                transform: static (attributeContext, token) => ServiceModelFactory.DescribeClosure(attributeContext, token))
            .Where(static model => !string.IsNullOrEmpty(model.Key)).Collect();

        // Registrations are intentionally emitted as one assembly-level registrar. Combine therefore forms an
        // all-model dependency, while each service model remains independently cacheable before this point.
        IncrementalValueProvider<string> source = serviceModels
            .Combine(singletonModels)
            .Combine(hostModels)
            .Combine(worldModels)
            .Combine(sceneModels)
            .Combine(transientModels)
            .Combine(scopedModels)
            .Combine(closureModels)
            .Select(static (models, token) => ServiceRegistrationEmitter.Render(
            [
                .. models.Left.Left.Left.Left.Left.Left.Left,
                .. models.Left.Left.Left.Left.Left.Left.Right,
                .. models.Left.Left.Left.Left.Left.Right,
                .. models.Left.Left.Left.Left.Right,
                .. models.Left.Left.Left.Right,
                .. models.Left.Left.Right,
                .. models.Left.Right,
                .. models.Right
            ], token))
            // A string has value equality, allowing Roslyn to reuse source output when recomputation produces
            // identical text (for example, after an unrelated edit).
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
