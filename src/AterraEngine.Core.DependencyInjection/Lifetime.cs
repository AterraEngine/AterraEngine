using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection;
/// <summary>A null scope type denotes a transient; other lifetimes are owner-anchored.</summary>
public readonly record struct Lifetime(Type? ScopeType) {
    public static Lifetime Transient => new(null);
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

public sealed class DependencyInjectionException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);
