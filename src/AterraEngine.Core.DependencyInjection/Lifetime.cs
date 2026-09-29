using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection;
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

/// <summary>Available only during the synchronous factory invocation. Do not retain or share it.</summary>
public interface IServiceResolver {
    T Get<T>() where T : notnull;
    object Get(Type serviceType);
}

/// <summary>Allocation-free resolver used by source-generated constructor activators.</summary>
public ref struct GeneratedServiceResolver {
    private readonly ServiceProvider _provider;
    private readonly ServiceProvider.ResolutionContext _context;
    private readonly OwnedScope _anchor;
    private readonly CacheSlot? _slot;

    internal GeneratedServiceResolver(
        ServiceProvider provider,
        ServiceProvider.ResolutionContext context,
        OwnedScope anchor,
        CacheSlot? slot
    ) {
        _provider = provider;
        _context = context;
        _anchor = anchor;
        _slot = slot;
    }

    public T Get<T>() where T : notnull => (T)_provider.ResolveGenerated(typeof(T), _context, _anchor, _slot);
    public object Get(Type serviceType) {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _provider.ResolveGenerated(serviceType, _context, _anchor, _slot);
    }
}

/// <summary>Constructor recipe emitted by the dependency-injection source generator.</summary>
public delegate object GeneratedServiceActivator(ref GeneratedServiceResolver resolver);

public sealed class DependencyInjectionException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);
