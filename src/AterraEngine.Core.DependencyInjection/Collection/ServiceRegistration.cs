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
    internal ServiceActivationPlan? Activator { get; set; }
    internal Func<IServiceResolver, object>? Factory { get; private init; }
    internal Func<object, object>? DecoratorFactory { get; private init; }
    internal object? Instance { get; private init; }
    public ServiceRegistration? Inner { get; private init; }
    public string Label => $"{Record.Service.Name}{(Key is {} serviceKey ? $" [key: {serviceKey.KeyType.Name}={serviceKey.Value ?? "<null>"}]" : "")} [module: {Record.Module ?? "<application>"}]";

    Func<IServiceResolver, object>? IServiceRegistration.Factory => Factory;
    ServiceActivationPlan? IServiceRegistration.Activator => Activator;
    Func<object, object>? IServiceRegistration.DecoratorFactory => DecoratorFactory;
    object? IServiceRegistration.Instance => Instance;

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

    internal static ServiceRegistration AsDecorator(
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
