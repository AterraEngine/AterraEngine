// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>A null scope type denotes a transient; other lifetimes are owner-anchored.</summary>
public readonly record struct ServiceLifetime(Type? ScopeType) {
    public static ServiceLifetime Transient => new(null);
    public static ServiceLifetime Singleton => Of<Singleton>();
    public static ServiceLifetime Host => Of<Host>();
    public static ServiceLifetime Of<TScope>() => new(typeof(TScope));
}
