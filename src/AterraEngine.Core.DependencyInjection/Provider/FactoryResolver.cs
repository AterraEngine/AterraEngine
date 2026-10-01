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
        => Check().Resolve<T>(context, anchor, cacheEntry);

    public T GetKeyed<T, TKey>(TKey key) where T : notnull
        => Check().ResolveKeyed<T, TKey>(key, context, anchor, cacheEntry);

    public T GetNamed<T>(string name) where T : notnull => GetKeyed<T, string>(name);

    internal void Close() => _open = false;

    private ServiceProvider Check() {
        if (!_open || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("A factory resolver may only be used synchronously during its factory invocation.");
        return provider;
    }
}
