// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AterraEngine.Core.DependencyInjection.Generators;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal static class ServiceModelFactory {
    private const string ServiceAttribute = "AterraEngine.Core.DependencyInjection.ServiceAttribute<TService>";
    private const string HostAttribute = "AterraEngine.Core.DependencyInjection.HostServiceAttribute<TService>";
    private const string SingletonAttribute = "AterraEngine.Core.DependencyInjection.SingletonServiceAttribute<TService>";
    private const string TransientAttribute = "AterraEngine.Core.DependencyInjection.TransientServiceAttribute<TService>";
    private const string WorldAttribute = "AterraEngine.Core.DependencyInjection.WorldServiceAttribute<TService>";
    private const string SceneAttribute = "AterraEngine.Core.DependencyInjection.SceneServiceAttribute<TService>";
    private const string ScopedAttribute = "AterraEngine.Core.DependencyInjection.ScopedServiceAttribute<TService, TScope>";
    private const string ConstructorAttribute = "AterraEngine.Core.DependencyInjection.ServiceConstructorAttribute";

    internal static (string Key, string Body, string Error) DescribeDeclaration(GeneratorSyntaxContext context, CancellationToken token) {
        var declaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, token) is not { } type) return default;

        ImmutableArray<AttributeData> attributes = type.GetAttributes().Where(IsServiceAttribute).ToImmutableArray();
        SyntaxReference? firstReference = attributes.IsEmpty ? null : attributes[0].ApplicationSyntaxReference;
        if (firstReference is null || firstReference.SyntaxTree != declaration.SyntaxTree ||
            !declaration.Span.Contains(firstReference.Span)) return default;
        return Describe(type, attributes, context.SemanticModel.Compilation, token);
    }

    internal static (string Key, string Body, string Error) Describe(
        INamedTypeSymbol type,
        ImmutableArray<AttributeData> attributes,
        Compilation compilation,
        CancellationToken token
    ) {
        string key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || type.IsFileLocal || type.TypeParameters.Length != 0 ||
            !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            return (key, "", $"Implementation '{key}' must be an accessible, non-generic concrete class.");

        IMethodSymbol[] marked = type.InstanceConstructors.Where(HasConstructorAttribute).ToArray();
        if (marked.Length > 1) return (key, "", $"Implementation '{key}' has more than one constructor marked [ServiceConstructor].");

        IMethodSymbol[] constructors = type.InstanceConstructors.Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public).ToArray();
        IMethodSymbol? selected = marked.SingleOrDefault();
        if (selected is not null && selected.DeclaredAccessibility != Accessibility.Public)
            return (key, "", $"The [ServiceConstructor] constructor for '{key}' must be public.");
        if (constructors.Length == 0)
            return (key, "", $"Implementation '{key}' must have a public constructor.");
        if (constructors.Length > 1 && selected is null)
            return (key, "", $"Implementation '{key}' has multiple public constructors; mark exactly one with [ServiceConstructor].");
        selected ??= constructors[0];

        if (selected.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType ||
            parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic))
            return (key, "", $"Constructor for '{key}' has unsupported ref, pointer, dynamic or ref-like parameters.");

        bool required = false;
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            required |= current.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true });
        if (required && !selected.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() ==
                "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
            return (key, "", $"Constructor for '{key}' must set required members (SetsRequiredMembers).");

        string[] dependencies = selected.Parameters.Select(parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToArray();
        var body = new StringBuilder();
        body.Append("        services.AddGeneratedActivator<").Append(key)
            .Append(">(static (ref global::AterraEngine.Core.DependencyInjection.GeneratedServiceResolver resolver) => new ")
            .Append(key).Append('(').Append(string.Join(", ", dependencies.Select(dependency => $"resolver.Get<{dependency}>()"))).Append(')');
        foreach (string dependency in dependencies) body.Append(", typeof(").Append(dependency).Append(')');
        body.AppendLine(");");

        var seenServices = new HashSet<string>(StringComparer.Ordinal);
        foreach (AttributeData attribute in attributes) {
            token.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.TypeArguments.FirstOrDefault() is not INamedTypeSymbol service) continue;
            string serviceName = service.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!seenServices.Add(serviceName))
                return (key, "", $"Implementation '{key}' registers service '{serviceName}' more than once.");
            var conversion = compilation.ClassifyCommonConversion(type, service);
            if (!SymbolEqualityComparer.Default.Equals(type, service) &&
                (!conversion.Exists || !conversion.IsImplicit || !conversion.IsReference))
                return (key, "", $"Implementation '{key}' is not assignable to service '{serviceName}'.");

            string attributeName = attribute.AttributeClass.OriginalDefinition.ToDisplayString();
            string lifetime;
            if (attributeName == HostAttribute) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Host";
            else if (attributeName == SingletonAttribute) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Singleton";
            else if (attributeName == TransientAttribute) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Transient";
            else if (attributeName == WorldAttribute) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.World>()";
            else if (attributeName == SceneAttribute) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.Scene>()";
            else if (attributeName == ScopedAttribute) {
                string scope = attribute.AttributeClass.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                lifetime = $"global::AterraEngine.Core.DependencyInjection.Lifetime.Of<{scope}>()";
            }
            else if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int value) {
                lifetime = value switch {
                    0 => "global::AterraEngine.Core.DependencyInjection.Lifetime.Transient",
                    1 => "global::AterraEngine.Core.DependencyInjection.Lifetime.Singleton",
                    2 => "global::AterraEngine.Core.DependencyInjection.Lifetime.Host",
                    3 => "global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.World>()",
                    4 => "global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.Scene>()",
                    _ => ""
                };
                if (lifetime.Length == 0) return (key, "", $"Implementation '{key}' has an invalid ServiceLifetime value.");
            }
            else return (key, "", $"Implementation '{key}' has an invalid service attribute.");

            body.Append("        services.Add<").Append(serviceName).Append(", ").Append(key).Append(">(").Append(lifetime).AppendLine(");");
        }

        return (key, body.ToString(), "");
    }

    internal static bool IsServiceAttribute(AttributeData attribute) {
        string? name = attribute.AttributeClass?.OriginalDefinition.ToDisplayString();
        return name is ServiceAttribute or HostAttribute or SingletonAttribute or TransientAttribute or WorldAttribute or SceneAttribute or ScopedAttribute;
    }

    private static bool HasConstructorAttribute(IMethodSymbol constructor)
        => constructor.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == ConstructorAttribute);
}
