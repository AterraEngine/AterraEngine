// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal sealed class ServiceRegistration(ServiceRecord record, ServiceKey? key = null) {
    internal ServiceRecord Record { get; } = record;
    internal ServiceKey? Key { get; } = key;
    internal ServiceActivationPlan? Activator { get; set; }
    internal Func<IServiceResolver, object>? Factory { get; private init; }
    internal Func<object, object>? DecoratorFactory { get; private init; }
    internal object? Instance { get; private init; }
    internal ServiceRegistration? Inner { get; private init; }
    internal ServiceInstanceOwnership Ownership { get; private init; } = ServiceInstanceOwnership.Container;
    internal string Label => $"{Record.Service.Name}{(Key is { } serviceKey ? $" [key: {serviceKey.KeyType.Name}={serviceKey.Value ?? "<null>"}]" : "") } [module: {Record.Module ?? "<application>"}]";

    // -----------------------------------------------------------------------------------------------------------------
    // Constructors
    // -----------------------------------------------------------------------------------------------------------------
    public static ServiceRegistration AsFactory(ServiceRecord record, Func<IServiceResolver, object> factory, ServiceKey? key = null)
        => new(record, key) {
            Factory = factory
        };
    
    public static ServiceRegistration AsInstance<T>(ServiceRecord record, T instance, ServiceInstanceOwnership ownership, ServiceKey? key = null)
        => new(record, key) {
            Instance = instance,
            Ownership = ownership
        };

    public static ServiceRegistration AsDecorator(ServiceRecord record, ServiceRegistration inner,
        Func<object, object>? factory, ServiceActivationPlan? activator, ServiceKey? key = null)
        => new(record, key) {
            Inner = inner,
            DecoratorFactory = factory,
            Activator = activator
        };
    
    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    internal DependencyInjectionException Error(string message, Exception? inner = null) => new($"{Label}: {message}", inner);
}
