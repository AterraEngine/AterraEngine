// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection.Scopes;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class Singleton;

public sealed class Host;

public sealed class World;

public sealed class Scene;

/// <summary>Typed, caller-owned input. The binding is immutable; use immutable value objects.</summary>
public sealed class ScopeInput {
    private ScopeInput(Type type, object value) {
        Type = type;
        Value = value;
    }
    public Type Type { get; }
    public object Value { get; }
    public static ScopeInput Of<T>(T value) where T : notnull =>
        new(typeof(T), value ?? throw new ArgumentNullException(nameof(value)));
}
