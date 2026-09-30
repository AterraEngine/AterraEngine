// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public interface IServiceRegistration {
    ServiceRecord Record { get; }
    ServiceKey? Key { get; }
    Func<IServiceResolver, object>? Factory { get; }
    ServiceActivationPlan? Activator { get; }
    Func<object, object>? DecoratorFactory { get; }
    object? Instance { get; }
    string Label { get; }
    ServiceRegistration? Inner { get; }
    DependencyInjectionException Error(string message, Exception? inner = null);
}
