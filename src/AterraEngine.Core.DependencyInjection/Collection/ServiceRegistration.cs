// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class ServiceRegistration(ServiceRecord record, ServiceKey? key = null) : IServiceRegistration {
    public ServiceInstanceOwnership Ownership { get; private init; } = ServiceInstanceOwnership.Container;
    public ServiceRecord Record { get; } = record;
    public ServiceKey? Key { get; } = key;
    public ServiceActivationPlan? Activator { get; set; }
    public Func<IServiceResolver, object>? Factory { get; private init; }
    public Func<object, object>? DecoratorFactory { get; private init; }
    public object? Instance { get; private init; }
    public ServiceRegistration? Inner { get; private init; }
    public string Label => $"{Record.Service.Name}{(Key is {} serviceKey ? $" [key: {serviceKey.KeyType.Name}={serviceKey.Value ?? "<null>"}]" : "")} [module: {Record.Module ?? "<application>"}]";

    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    public DependencyInjectionException Error(string message, Exception? inner = null)
        => new($"{Label}: {message}", inner);

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

    public static ServiceRegistration AsDecorator(
        ServiceRecord record,
        ServiceRegistration inner,
        Func<object, object>? factory,
        ServiceActivationPlan? activator,
        ServiceKey? key = null
    )
        => new(record, key) {
            Inner = inner,
            DecoratorFactory = factory,
            Activator = activator
        };
}
