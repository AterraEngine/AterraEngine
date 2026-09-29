// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection.Collection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal sealed class ServiceRegistration(ServiceRecord record) {
    internal ServiceRecord Record { get; } = record;
    internal ServiceActivationPlan? Activator { get; set; }
    internal Func<IServiceResolver, object>? Factory { get; private init; }
    internal object? Instance { get; private init; }
    internal InstanceOwnership Ownership { get; private init; } = InstanceOwnership.Container;
    internal string Label => $"{Record.Service.Name} [module: {Record.Module ?? "<application>"}]";

    // -----------------------------------------------------------------------------------------------------------------
    // Constructors
    // -----------------------------------------------------------------------------------------------------------------
    public static ServiceRegistration AsFactory(ServiceRecord record, Func<IServiceResolver, object> factory) 
        => new(record) {
            Factory = factory
        };
    
    public static ServiceRegistration AsInstance<T>(ServiceRecord record, T instance, InstanceOwnership ownership)
        => new(record) {
            Instance = instance,
            Ownership = ownership
        };
    
    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    internal DependencyInjectionException Error(string message, Exception? inner = null) => new($"{Label}: {message}", inner);
}
