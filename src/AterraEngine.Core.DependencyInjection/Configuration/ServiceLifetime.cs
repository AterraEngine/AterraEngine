// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>A null scope type denotes a transient; other lifetimes are owner-anchored.</summary>
public readonly record struct ServiceLifetime(Type? ScopeType) {
    public static ServiceLifetime Transient => new(null);
    public static ServiceLifetime Singleton => Of<AterraSingleton>();
    public static ServiceLifetime Host => Of<AterraHost>();
    public static ServiceLifetime Of<TScope>() => new(typeof(TScope));
}
