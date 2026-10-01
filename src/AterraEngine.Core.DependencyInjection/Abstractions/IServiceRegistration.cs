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
    internal Func<IServiceResolver, object>? Factory { get; }
    internal ServiceActivationPlan? Activator { get; }
    internal Func<object, object>? DecoratorFactory { get; }
    internal object? Instance { get; }
    string Label { get; }
    ServiceRegistration? Inner { get; }
    DependencyInjectionException Error(string message, Exception? inner = null);
}
