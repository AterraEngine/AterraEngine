// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Allocation-free resolver used by source-generated constructor activators.</summary>
public readonly ref struct GeneratedServiceResolver(
    ServiceProvider provider,
    ServiceResolutionContext context,
    OwnedServiceScope anchor,
    ServiceCacheEntry? cacheEntry
){

    public T Get<T>() where T : notnull => (T)provider.ResolveGenerated(typeof(T), context, anchor, cacheEntry);
    public object Get(Type serviceType) {
        ArgumentNullException.ThrowIfNull(serviceType);
        return provider.ResolveGenerated(serviceType, context, anchor, cacheEntry);
    }
}
