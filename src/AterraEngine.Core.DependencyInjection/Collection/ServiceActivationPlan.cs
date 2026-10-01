// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed record ServiceActivationPlan(
    Func<IServiceResolver, object>? Create,
    Delegate? GeneratedCreate,
    Type[] Dependencies
) {
    internal ITypedGeneratedActivator? TypedGeneratedCreate { get; set; }
}
