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
    ServiceCacheEntry? cacheEntry,
    IServiceRegistration? inner = null
) {
    public T Get<T>() where T : notnull
        => provider.ResolveGenerated<T>(context, anchor, cacheEntry);

    public T GetInner<T>() where T : notnull
        => provider.ResolveGeneratedInner<T>(inner, context, anchor, cacheEntry);

    public T[] GetAll<T>()
        => provider.ResolveGeneratedCollection<T>(context, anchor, cacheEntry);

    public T GetKeyed<T, TKey>(TKey key) where T : notnull
        => provider.ResolveGeneratedKeyed<T, TKey>(key, context, anchor, cacheEntry);

    public T GetNamed<T>(string name) where T : notnull
        => GetKeyed<T, string>(name);

    public T[] GetAllKeyed<T, TKey>(TKey key)
        => provider.ResolveGeneratedKeyedCollection<T, TKey>(key, context, anchor, cacheEntry);

}
