// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Reflection;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Single-threaded configuration, frozen after a successful Build.</summary>
public sealed class ServiceCollection : IServiceCollection {
    private readonly Dictionary<Type, ServiceActivationPlan> _activators = [];
    private readonly Dictionary<Type, GeneratedServiceCollectionResolver> _collectionResolvers = [];
    private readonly Dictionary<Type, Type> _inputs = [];
    private readonly Dictionary<ServiceKey, List<ServiceRegistration>> _keyedRegistrationSets = [];
    private readonly Dictionary<ServiceKey, ServiceRegistration> _keyedRegistrations = [];
    private readonly Dictionary<Type, Type[]> _parents = new() {
        [typeof(AterraSingleton)] = [],
        [typeof(AterraHost)] = [typeof(AterraSingleton)],
        [typeof(AterraWorld)] = [typeof(AterraHost)],
        [typeof(AterraScene)] = [typeof(AterraWorld)]
    };
    private readonly Dictionary<Type, List<ServiceRegistration>> _registrationSets = [];
    private readonly Dictionary<Type, ServiceRegistration> _registrations = [];
    private bool _built;
    private ServiceDiagnosticsOptions? _diagnostics;

    private string? Module { get; set; }

    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    /// <summary>Enables immutable diagnostics for the provider built from this collection.</summary>
    public IServiceCollection ConfigureDiagnostics(ServiceDiagnosticsOptions options) {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Sink);
        _diagnostics = options;
        return this;
    }

    /// <summary>Applies generated service registrations from the assembly containing <typeparamref name="TAssemblyMarker" />.</summary>
    public IServiceCollection RegisterActivators<TAssemblyMarker>()
        => RegisterActivators(typeof(TAssemblyMarker).Assembly);

    /// <summary>Applies generated service registrations from <paramref name="assembly" />.</summary>
    public IServiceCollection RegisterActivators(Assembly assembly) {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(assembly);
        GeneratedServiceRegistration.Apply(assembly, this);
        return this;
    }

    public IServiceCollection AddGeneratedCollectionResolver<T>(GeneratedServiceCollectionResolver resolver) {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(resolver);
        _collectionResolvers[typeof(T)] = resolver;
        return this;
    }

    public IServiceCollection Add(ServiceRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);

        ServiceRecord alteredRecord = record with { Module = record.Module ?? Module };
        var registration = new ServiceRegistration(alteredRecord);
        return AddRegistration(registration);
    }

    public IServiceCollection AddEnumerable(ServiceRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);
        return AddOrAppendRegistration(new ServiceRegistration(record with { Module = record.Module ?? Module }));
    }

    public IServiceCollection AddFactory(ServiceRecord record, Func<IServiceResolver, object> factory, bool enumerable) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);
        ArgumentNullException.ThrowIfNull(factory);
        ServiceRegistration registration = ServiceRegistration.AsFactory(record with { Module = record.Module ?? Module }, factory);
        return enumerable ? AddOrAppendRegistration(registration) : AddRegistration(registration);
    }

    public IServiceCollection AddInstance(ServiceRecord record, object instance, ServiceInstanceOwnership ownership, bool enumerable) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

        var registration = ServiceRegistration.AsInstance(record with { Module = record.Module ?? Module }, instance, ownership);
        return enumerable ? AddOrAppendRegistration(registration) : AddRegistration(registration);
    }

    public IServiceCollection AddKeyed(ServiceRecord record, ServiceKey key, bool enumerable) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);
        ServiceRegistration registration = new(record with { Module = record.Module ?? Module }, key);
        return enumerable ? AddOrAppendKeyedRegistration(registration) : RegisterKeyedRegistration(registration);
    }

    public IServiceCollection AddKeyedFactory(ServiceRecord record, ServiceKey key, Func<IServiceResolver, object> factory, bool enumerable) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);
        ArgumentNullException.ThrowIfNull(factory);
        ServiceRegistration registration = ServiceRegistration.AsFactory(record with { Module = record.Module ?? Module }, factory, key);
        return enumerable ? AddOrAppendKeyedRegistration(registration) : RegisterKeyedRegistration(registration);
    }

    public IServiceCollection AddKeyedInstance(ServiceRecord record, ServiceKey key, object instance, ServiceInstanceOwnership ownership, bool enumerable) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

        var registration = ServiceRegistration.AsInstance(record with { Module = record.Module ?? Module }, instance, ownership, key);
        return enumerable ? AddOrAppendKeyedRegistration(registration) : RegisterKeyedRegistration(registration);
    }

    /// <summary>Installs a generated (or explicitly authored) constructor recipe, not a service registration.</summary>
    public IServiceCollection AddActivator<T>(Func<IServiceResolver, T> create, params Type[] dependencies) where T : class {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfContainsAnyNull<Type[], Type>(dependencies);

        var activator = new ServiceActivationPlan(create, null, dependencies.ToArray());

        return _activators.TryAdd(typeof(T), activator)
            ? this
            : throw new DependencyInjectionException($"An activator for {typeof(T)} is already installed.");
    }

    /// <summary>Installs an allocation-free constructor recipe emitted by the source generator.</summary>
    public IServiceCollection AddGeneratedActivator<T>(GeneratedServiceActivator create, params Type[] dependencies) where T : class {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfContainsAnyNull<Type[], Type>(dependencies);

        var activator = new ServiceActivationPlan(null, create, dependencies.ToArray());
        return _activators.TryAdd(typeof(T), activator)
            ? this
            : throw new DependencyInjectionException($"An activator for {typeof(T)} is already installed.");
    }

    /// <summary>Opaque factory dependencies are checked at runtime, not during Build.</summary>
    public IServiceCollection Decorate(Type service, ServiceKey? key, Type decorator, Func<object, object>? factory, GeneratedServiceActivator? create, Type[] dependencies) {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(decorator);
        ArgumentNullException.ThrowIfNull(dependencies);
        ServiceActivationPlan? activator = create is null ? null : new ServiceActivationPlan(null, create, dependencies);
        return DecorateCore(service, key, decorator, factory, activator);
    }

    public IServiceCollection DecorateGenerated<TService, TDecorator>(ServiceKey? key)
        where TService : class
        where TDecorator : class, TService
        => DecorateGeneratedCore<TService, TDecorator>(key);

    public IServiceCollection AddModule(string name, Action<ServiceCollection> configure) {
        ThrowIfNotMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        string? previous = Module;
        Module = name;
        try {
            configure(this);
        }
        finally {
            // Reset if failed
            Module = previous;
        }

        return this;
    }

    public IServiceCollection DeclareScope<TScope>(params Type[] allowedParents) {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(allowedParents);
        ArgumentNullException.ThrowIfContainsAnyNull<Type[], Type>(allowedParents);

        return _parents.TryAdd(typeof(TScope), allowedParents.ToArray())
            ? this
            : throw new DependencyInjectionException($"Scope {typeof(TScope)} is already declared.");
    }

    public IServiceCollection RequireInput<TScope, TInput>() where TInput : notnull {
        ThrowIfNotMutable();

        if (ServiceProvider.IsProviderService(typeof(TInput)))
            throw new DependencyInjectionException($"Input {typeof(TInput)} conflicts with the built-in provider service.");
        if (_registrations.ContainsKey(typeof(TInput)) || !_inputs.TryAdd(typeof(TInput), typeof(TScope)))
            throw new DependencyInjectionException($"Input {typeof(TInput)} conflicts with an existing service or input.");

        return this;
    }

    public ServiceProvider Build(params ServiceScopeInput[] hostInputs) {
        ThrowIfNotMutable();
        if (Module is not null) throw new DependencyInjectionException("Build cannot run inside a module contribution.");

        ServiceCollectionValidator.Validate(_activators, _inputs, _parents, _registrations, _registrationSets,
            _keyedRegistrations.Values, EnumerateRegistrationSets(_keyedRegistrationSets));

        var registrations = new Dictionary<Type, ServiceRegistration>(_registrations.Count);
        foreach ((Type service, ServiceRegistration registration) in _registrations) {
            registrations.Add(service, registration);
        }

        var registrationSets = new Dictionary<Type, ServiceRegistration[]>(_registrationSets.Count);
        foreach ((Type service, List<ServiceRegistration> values) in _registrationSets) {
            var copy = new ServiceRegistration[values.Count];
            values.CopyTo(copy);
            registrationSets.Add(service, copy);
        }

        var keyedRegistrations = new Dictionary<ServiceKey, ServiceRegistration>(_keyedRegistrations.Count);
        foreach ((ServiceKey key, ServiceRegistration registration) in _keyedRegistrations) {
            keyedRegistrations.Add(key, registration);
        }

        var keyedRegistrationSets = new Dictionary<ServiceKey, ServiceRegistration[]>(_keyedRegistrationSets.Count);
        foreach ((ServiceKey key, List<ServiceRegistration> values) in _keyedRegistrationSets) {
            var copy = new ServiceRegistration[values.Count];
            values.CopyTo(copy);
            keyedRegistrationSets.Add(key, copy);
        }

        var collectionResolvers = new Dictionary<Type, GeneratedServiceCollectionResolver>(_collectionResolvers.Count);
        foreach ((Type service, GeneratedServiceCollectionResolver resolver) in _collectionResolvers) {
            collectionResolvers.Add(service, resolver);
        }

        var parents = new Dictionary<Type, Type[]>(_parents.Count);
        foreach ((Type scope, Type[] values) in _parents) {
            var copy = new Type[values.Length];
            Array.Copy(values, copy, values.Length);
            parents.Add(scope, copy);
        }

        var inputOwners = new Dictionary<Type, Type>(_inputs.Count);
        foreach ((Type input, Type scope) in _inputs) inputOwners.Add(input, scope);

        var provider = new ServiceProvider(registrations, registrationSets, keyedRegistrations, keyedRegistrationSets, collectionResolvers, parents, inputOwners, hostInputs, _diagnostics);
        _built = true;

        return provider;

        static IEnumerable<ServiceRegistration> EnumerateRegistrationSets(
            IReadOnlyDictionary<ServiceKey, List<ServiceRegistration>> sets
        ) {
            foreach (List<ServiceRegistration> values in sets.Values) {
                foreach (ServiceRegistration t in values) {
                    yield return t;
                }
            }
        }
    }

    private IServiceCollection DecorateCore(Type service, ServiceKey? key, Type decorator, Func<object, object>? factory, ServiceActivationPlan? activator) {
        ThrowIfNotMutable();
        if (decorator.ContainsGenericParameters || service.ContainsGenericParameters)
            throw new DependencyInjectionException($"Open-generic decoration is not supported for {service} with {decorator}; register a closed typed decoration.");

        if (key is null) {
            if (!_registrationSets.TryGetValue(service, out List<ServiceRegistration>? current) || current.Count == 0)
                throw new DependencyInjectionException($"Cannot decorate unregistered service {service}.");

            ServiceRegistration[] wrapped = current.Select(registration => Wrap(registration, decorator, factory, activator)).ToArray();
            _registrationSets[service] = wrapped.ToList();
            _registrations[service] = wrapped[^1];
        }
        else {
            if (!_keyedRegistrationSets.TryGetValue(key.Value, out List<ServiceRegistration>? current) || current.Count == 0)
                throw new DependencyInjectionException($"Cannot decorate unregistered keyed service {key.Value}.");

            ServiceRegistration[] wrapped = current.Select(registration => Wrap(registration, decorator, factory, activator)).ToArray();
            _keyedRegistrationSets[key.Value] = wrapped.ToList();
            _keyedRegistrations[key.Value] = wrapped[^1];
        }

        return this;
    }

    internal IServiceCollection DecorateGeneratedCore<TService, TDecorator>(ServiceKey? key)
        where TService : class
        where TDecorator : class, TService {

        if (!_activators.TryGetValue(typeof(TDecorator), out ServiceActivationPlan? activator) || activator.GeneratedCreate is null)
            throw new DependencyInjectionException($"No generated constructor activator is registered for decorator {typeof(TDecorator)}. Register its generated activator or use the factory overload.");

        return !activator.Dependencies.Contains(typeof(TService))
            ? DecorateCore(typeof(TService), key, typeof(TDecorator), null, activator)
            : throw new DependencyInjectionException($"Generated decorator {typeof(TDecorator)} requests {typeof(TService)} as a public dependency. Mark the constructor parameter as a decorated dependency or use GetInner<TService>().");

    }

    private static ServiceRegistration Wrap(ServiceRegistration inner, Type decorator, Func<object, object>? factory, ServiceActivationPlan? activator)
        => decorator.IsAssignableTo(inner.Record.Service)
            ? ServiceRegistration.AsDecorator(inner.Record with { Implementation = decorator }, inner, factory, activator, inner.Key)
            : throw new DependencyInjectionException($"Decorator {decorator} is not assignable to service {inner.Record.Service}.");

    internal IServiceCollection AddRegistration(ServiceRegistration registration) {
        ThrowIfNotMutable();
        Type service = registration.Record.Service;
        if (ServiceProvider.IsProviderService(service))
            throw registration.Error("Conflicts with the built-in provider service.");
        if (_inputs.ContainsKey(service)) throw registration.Error("Conflicts with a declared input.");

        _registrations[service] = registration;
        _registrationSets[service] = [registration];
        return this;
    }

    internal IServiceCollection AddOrAppendRegistration(ServiceRegistration registration) {
        ThrowIfNotMutable();
        Type service = registration.Record.Service;
        if (ServiceProvider.IsProviderService(service))
            throw registration.Error("Conflicts with the built-in provider service.");
        if (_inputs.ContainsKey(service)) throw registration.Error("Conflicts with a declared input.");

        if (!_registrationSets.TryGetValue(service, out List<ServiceRegistration>? registrations))
            _registrationSets[service] = registrations = [];
        registrations.Add(registration);
        _registrations[service] = registration;
        return this;
    }

    internal IServiceCollection RegisterKeyedRegistration(ServiceRegistration registration) {
        ThrowIfNotMutable();
        Type service = registration.Record.Service;
        if (ServiceProvider.IsProviderService(service)) throw registration.Error("Conflicts with the built-in provider service.");
        if (_inputs.ContainsKey(service)) throw registration.Error("Conflicts with a declared input.");

        _keyedRegistrations[registration.Key!.Value] = registration;
        _keyedRegistrationSets[registration.Key.Value] = [registration];
        return this;
    }

    internal IServiceCollection AddOrAppendKeyedRegistration(ServiceRegistration registration) {
        ThrowIfNotMutable();
        Type service = registration.Record.Service;
        if (ServiceProvider.IsProviderService(service)) throw registration.Error("Conflicts with the built-in provider service.");
        if (_inputs.ContainsKey(service)) throw registration.Error("Conflicts with a declared input.");

        ServiceKey key = registration.Key!.Value;
        if (!_keyedRegistrationSets.TryGetValue(key, out List<ServiceRegistration>? registrations)) _keyedRegistrationSets[key] = registrations = [];
        registrations.Add(registration);
        _keyedRegistrations[key] = registration;
        return this;
    }

    private void ThrowIfNotMutable() {
        if (_built) throw new InvalidOperationException("Configuration is immutable after Build.");
    }
}
