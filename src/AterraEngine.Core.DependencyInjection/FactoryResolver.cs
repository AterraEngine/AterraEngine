// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class FactoryResolver(
    ServiceProvider provider,
    ServiceResolutionContext context,
    OwnedScope anchor,
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
    
    internal void Close() => _open = false;
}
