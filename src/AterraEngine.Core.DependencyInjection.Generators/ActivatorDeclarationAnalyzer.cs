// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AterraEngine.Core.DependencyInjection.Generators;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ActivatorDeclarationAnalyzer : DiagnosticAnalyzer {
    private static readonly DiagnosticDescriptor InvalidDeclaration = new("ADI001", "Invalid service activator declaration",
        "{0}", "Aterra.DependencyInjection", DiagnosticSeverity.Error, true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [InvalidDeclaration];

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // A symbol action observes a partial type once and avoids rebinding every attributed syntax declaration.
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context) {
        var type = (INamedTypeSymbol)context.Symbol;
        ImmutableArray<AttributeData> attributes = type.GetAttributes().Where(ServiceModelFactory.IsServiceAttribute).ToImmutableArray();
        if (attributes.IsEmpty) {
            ImmutableArray<AttributeData> closures = type.GetAttributes().Where(ServiceModelFactory.IsClosureAttribute).ToImmutableArray();
            foreach (AttributeData closure in closures) {
                (string _, string _, string closureError) = ServiceModelFactory.DescribeClosure(
                    type, closure, context.Compilation, context.CancellationToken);
                if (closureError.Length != 0)
                    context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration,
                        closure.ApplicationSyntaxReference!.GetSyntax(context.CancellationToken).GetLocation(), closureError));
            }

            return;
        }

        // Reuse the generator's validation contract while skipping registration-body rendering.
        // Keeping diagnostics in an analyzer gives users a precise source location,
        // without putting Locations into incremental models.
        (string _, string _, string error) = ServiceModelFactory.Describe(
            type, attributes, context.Compilation, context.CancellationToken, false);
        if (error.Length != 0)
            context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration,
                attributes[0].ApplicationSyntaxReference!.GetSyntax(context.CancellationToken).GetLocation(), error));
    }
}
