// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Reflection;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public interface IServiceCollection {
    IServiceCollection ConfigureDiagnostics(ServiceDiagnosticsOptions options);
    IServiceCollection RegisterActivators<TAssemblyMarker>();
    IServiceCollection RegisterActivators(Assembly assembly);
    IServiceCollection AddGeneratedCollectionResolver<T>(GeneratedServiceCollectionResolver resolver);
    IServiceCollection AddActivator<T>(Func<IServiceResolver, T> create, params Type[] dependencies) where T : class;
    IServiceCollection AddGeneratedActivator<T>(GeneratedServiceActivator create, params Type[] dependencies) where T : class;
    IServiceCollection Add(ServiceRecord record);
    IServiceCollection AddEnumerable(ServiceRecord record);
    IServiceCollection AddFactory(ServiceRecord record, Func<IServiceResolver, object> factory, bool enumerable);
    IServiceCollection AddInstance(ServiceRecord record, object instance, ServiceInstanceOwnership ownership, bool enumerable);
    IServiceCollection AddKeyed(ServiceRecord record, ServiceKey key, bool enumerable);
    IServiceCollection AddKeyedFactory(ServiceRecord record, ServiceKey key, Func<IServiceResolver, object> factory, bool enumerable);
    IServiceCollection AddKeyedInstance(ServiceRecord record, ServiceKey key, object instance, ServiceInstanceOwnership ownership, bool enumerable);
    IServiceCollection Decorate(Type service, ServiceKey? key, Type decorator, Func<object, object>? factory, GeneratedServiceActivator? create, Type[] dependencies);
    IServiceCollection DecorateGenerated<TService, TDecorator>(ServiceKey? key)
        where TService : class where TDecorator : class, TService;
    IServiceCollection AddModule(string name, Action<ServiceCollection> configure);
    IServiceCollection DeclareScope<TScope>(params Type[] allowedParents);
    IServiceCollection RequireInput<TScope, TInput>() where TInput : notnull;
    ServiceProvider Build(params ServiceScopeInput[] hostInputs);
}
