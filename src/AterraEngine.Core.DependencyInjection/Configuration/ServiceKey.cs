namespace AterraEngine.Core.DependencyInjection;
/// <summary>
///     Composite identity for a keyed service. Key type is part of identity, so boxed values of different types do
///     not collide.
/// </summary>
public readonly record struct ServiceKey(Type ServiceType, Type KeyType, object? Value) {
    public static ServiceKey Of<TService, TKey>(TKey key) => new(typeof(TService), typeof(TKey), key);
    public static ServiceKey OfRuntime<TService>(object? key) => new(typeof(TService), key?.GetType() ?? typeof(object), key);

    public override string ToString() => $"{ServiceType} (key {KeyType}: {Value ?? "<null>"})";
}
