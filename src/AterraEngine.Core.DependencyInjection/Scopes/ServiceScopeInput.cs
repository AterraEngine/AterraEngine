// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Typed, caller-owned input. The binding is immutable; use immutable value objects.</summary>
public sealed class ServiceScopeInput {
    private ServiceScopeInput(Type type, object value) {
        Type = type;
        Value = value;
    }
    public Type Type { get; }
    public object Value { get; }
    public static ServiceScopeInput Of<T>(T value) where T : notnull =>
        new(typeof(T), value ?? throw new ArgumentNullException(nameof(value)));
}
