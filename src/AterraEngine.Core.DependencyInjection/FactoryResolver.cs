// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class FactoryResolver(
    ServiceProvider provider,
    ServiceResolutionContext context,
    OwnedServiceScope anchor,
    ServiceCacheEntry? cacheEntry
) : IServiceResolver {
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _open = true;

    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    public T Get<T>() where T : notnull
        => (T)Get(typeof(T));
    
    public object Get(Type serviceType) {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (!_open || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("A factory resolver may only be used synchronously during its factory invocation.");

        return provider.Resolve(serviceType, context, anchor, cacheEntry);
    }

    public T GetKeyed<T, TKey>(TKey key) where T : notnull => (T)GetKeyed(typeof(T), typeof(TKey), key);
    public T GetNamed<T>(string name) where T : notnull => GetKeyed<T, string>(name);
    public object GetKeyed(Type serviceType, Type keyType, object? key) {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(keyType);
        if (!_open || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("A factory resolver may only be used synchronously during its factory invocation.");
        return provider.ResolveGeneratedKeyed(new ServiceKey(serviceType, keyType, key), context, anchor, cacheEntry);
    }
    
    internal void Close() => _open = false;
}
