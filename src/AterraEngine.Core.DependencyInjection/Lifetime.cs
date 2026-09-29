// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>A null scope type denotes a transient; other lifetimes are owner-anchored.</summary>
public readonly record struct Lifetime(Type? ScopeType) {
    public static Lifetime Transient => new(null);
    public static Lifetime Singleton => Of<Singleton>();
    public static Lifetime Host => Of<Host>();
    public static Lifetime Of<TScope>() => new(typeof(TScope));
}

public enum InstanceOwnership {
    Caller,
    Container
}
