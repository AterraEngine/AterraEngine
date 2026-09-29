namespace AterraEngine.Core.DependencyInjection;
/// <summary>
///     Generates AddActivators(ServiceCollection) on a top-level static partial class.
///     Implementations must have exactly one public constructor; dependencies remain explicit registrations.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GenerateServiceActivatorsAttribute(params Type[] implementations) : Attribute {
    public IReadOnlyList<Type> Implementations { get; } = Array.AsReadOnly(implementations);
}
