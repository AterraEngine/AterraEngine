// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context) {
        var declaration = (TypeDeclarationSyntax)context.Node;
        if (declaration.AttributeLists.Count == 0 || context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } type)
            return;

        ImmutableArray<AttributeData> attributes = type.GetAttributes().Where(ServiceModelFactory.IsServiceAttribute).ToImmutableArray();
        SyntaxReference? firstReference = attributes.IsEmpty ? null : attributes[0].ApplicationSyntaxReference;
        if (firstReference is null || firstReference.SyntaxTree != declaration.SyntaxTree ||
            !declaration.Span.Contains(firstReference.Span)) return;

        (string _, string _, string error) = ServiceModelFactory.Describe(type, attributes, context.Compilation, context.CancellationToken);
        if (error.Length != 0)
            context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration,
                attributes[0].ApplicationSyntaxReference!.GetSyntax(context.CancellationToken).GetLocation(), error));
    }
}
