// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace AterraEngine.Core.DependencyInjection.Generators;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal static class ServiceModelFactory {
    internal const string ServiceAttributeMetadataName = "AterraEngine.Core.DependencyInjection.ServiceAttribute`1";
    internal const string HostAttributeMetadataName = "AterraEngine.Core.DependencyInjection.HostServiceAttribute`1";
    internal const string SingletonAttributeMetadataName = "AterraEngine.Core.DependencyInjection.SingletonServiceAttribute`1";
    internal const string TransientAttributeMetadataName = "AterraEngine.Core.DependencyInjection.TransientServiceAttribute`1";
    internal const string WorldAttributeMetadataName = "AterraEngine.Core.DependencyInjection.WorldServiceAttribute`1";
    internal const string SceneAttributeMetadataName = "AterraEngine.Core.DependencyInjection.SceneServiceAttribute`1";
    internal const string ScopedAttributeMetadataName = "AterraEngine.Core.DependencyInjection.ScopedServiceAttribute`2";
    private const string ConstructorAttribute = "AterraEngine.Core.DependencyInjection.ServiceConstructorAttribute";

    internal static (string Key, string Body, string Error) DescribeAttributedType(
        GeneratorAttributeSyntaxContext context,
        int markerOrder,
        CancellationToken token
    ) {
        if (context.TargetSymbol is not INamedTypeSymbol type) return default;

        ImmutableArray<AttributeData> attributes = type.GetAttributes().Where(IsServiceAttribute).ToImmutableArray();
        if (attributes.IsEmpty || attributes.Min(GetMarkerOrder) != markerOrder) return default;

        return Describe(type, attributes, context.SemanticModel.Compilation, token);
    }

    internal static (string Key, string Body, string Error) Describe(
        INamedTypeSymbol type,
        ImmutableArray<AttributeData> attributes,
        Compilation compilation,
        CancellationToken token,
        bool render = true
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
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType) {
            required |= current.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true });
        }

        if (required && !selected.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() ==
            "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
            return (key, "", $"Constructor for '{key}' must set required members (SetsRequiredMembers).");

        StringBuilder? body = null;
        if (render) {
            string[] dependencies = selected.Parameters.Select(parameter =>
                parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToArray();
            body = new StringBuilder();
            body.Append("        services.AddGeneratedActivator<").Append(key)
                .Append(">(static (ref global::AterraEngine.Core.DependencyInjection.GeneratedServiceResolver resolver) => new ")
                .Append(key).Append('(').Append(string.Join(", ", dependencies.Select(dependency => $"resolver.Get<{dependency}>()"))).Append(')');
            foreach (string dependency in dependencies) body.Append(", typeof(").Append(dependency).Append(')');
            body.AppendLine(");");
        }

        var seenServices = new HashSet<string>(StringComparer.Ordinal);
        List<(string Service, string Lifetime)>? registrations = render ? new List<(string Service, string Lifetime)>() : null;
        foreach (AttributeData attribute in attributes) {
            token.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.TypeArguments.FirstOrDefault() is not {} service)
                return (key, "", $"Implementation '{key}' has an invalid service attribute.");

            string serviceName = service.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!seenServices.Add(serviceName))
                return (key, "", $"Implementation '{key}' registers service '{serviceName}' more than once.");

            CommonConversion conversion = compilation.ClassifyCommonConversion(type, service);
            if (!SymbolEqualityComparer.Default.Equals(type, service) &&
                (!conversion.Exists || !conversion.IsImplicit || !conversion.IsReference))
                return (key, "", $"Implementation '{key}' is not assignable to service '{serviceName}'.");

            string attributeName = GetMetadataName(attribute.AttributeClass.OriginalDefinition);
            string lifetime;
            if (attributeName == HostAttributeMetadataName) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Host";
            else if (attributeName == SingletonAttributeMetadataName) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Singleton";
            else if (attributeName == TransientAttributeMetadataName) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Transient";
            else if (attributeName == WorldAttributeMetadataName) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.World>()";
            else if (attributeName == SceneAttributeMetadataName) lifetime = "global::AterraEngine.Core.DependencyInjection.Lifetime.Of<global::AterraEngine.Core.DependencyInjection.Scopes.Scene>()";
            else if (attributeName == ScopedAttributeMetadataName) {
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

            registrations?.Add((serviceName, lifetime));
        }

        if (body is not null) {
            foreach ((string service, string lifetime) in registrations!.OrderBy(
                keySelector: registration => registration.Service, StringComparer.Ordinal)) {
                body.Append("        services.Add<").Append(service).Append(", ").Append(key).Append(">(")
                    .Append(lifetime).AppendLine(");");
            }
        }

        return (key, body?.ToString() ?? "", "");
    }

    internal static bool IsServiceAttribute(AttributeData attribute) {
        string? name = attribute.AttributeClass is {} attributeClass ? GetMetadataName(attributeClass.OriginalDefinition) : null;
        return name is ServiceAttributeMetadataName or HostAttributeMetadataName or SingletonAttributeMetadataName or
            TransientAttributeMetadataName or WorldAttributeMetadataName or SceneAttributeMetadataName or ScopedAttributeMetadataName;
    }

    private static int GetMarkerOrder(AttributeData attribute) => GetMetadataName(attribute.AttributeClass!.OriginalDefinition) switch {
        ServiceAttributeMetadataName => 0,
        SingletonAttributeMetadataName => 1,
        HostAttributeMetadataName => 2,
        WorldAttributeMetadataName => 3,
        SceneAttributeMetadataName => 4,
        TransientAttributeMetadataName => 5,
        ScopedAttributeMetadataName => 6,
        _ => int.MaxValue
    };

    private static string GetMetadataName(INamedTypeSymbol type) {
        string namespaceName = type.ContainingNamespace.ToDisplayString();
        return namespaceName.Length == 0 ? type.MetadataName : namespaceName + "." + type.MetadataName;
    }

    private static bool HasConstructorAttribute(IMethodSymbol constructor)
        => constructor.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == ConstructorAttribute);
}
