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
        => (T)provider.ResolveGenerated(typeof(T), context, anchor, cacheEntry);

    public T GetInner<T>() where T : notnull
        => (T)provider.ResolveGeneratedInner<T>(inner, context, anchor, cacheEntry);

    public T[] GetAll<T>()
        => provider.ResolveGeneratedCollection<T>(context, anchor, cacheEntry);

    public T GetKeyed<T, TKey>(TKey key) where T : notnull
        => (T)provider.ResolveGeneratedKeyed(ServiceKey.Of<T, TKey>(key), context, anchor, cacheEntry);

    public T GetNamed<T>(string name) where T : notnull
        => GetKeyed<T, string>(name);

    public T[] GetAllKeyed<T, TKey>(TKey key)
        => provider.ResolveGeneratedKeyedCollection<T, TKey>(key, context, anchor, cacheEntry);

    public object Get(Type serviceType) {
        ArgumentNullException.ThrowIfNull(serviceType);
        return provider.ResolveGenerated(serviceType, context, anchor, cacheEntry);
    }

    public object GetKeyed(Type serviceType, Type keyType, object? key) {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(keyType);
        return provider.ResolveGeneratedKeyed(new ServiceKey(serviceType, keyType, key), context, anchor, cacheEntry);
    }
}
