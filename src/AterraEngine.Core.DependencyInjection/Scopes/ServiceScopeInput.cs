// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Typed, caller-owned input. The binding is immutable; use immutable value objects.</summary>
public abstract class ServiceScopeInput {
    internal abstract Type Type { get; }
    internal abstract object UntypedValue { get; }

    public static ServiceScopeInput<T> Of<T>(T value) where T : notnull =>
        new(value ?? throw new ArgumentNullException(nameof(value)));
}

public sealed class ServiceScopeInput<T>(T value) : ServiceScopeInput where T : notnull {
    public T Value { get; } = value;
    internal override Type Type => typeof(T);
    internal override object UntypedValue => Value;
}
