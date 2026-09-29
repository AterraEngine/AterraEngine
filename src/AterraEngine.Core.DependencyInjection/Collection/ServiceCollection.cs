// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Reflection;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Single-threaded configuration, frozen after a successful Build.</summary>
public sealed class ServiceCollection {
    private readonly Dictionary<Type, ServiceActivationPlan> _activators = [];
    private readonly Dictionary<Type, Type> _inputs = [];
    private readonly Dictionary<Type, Type[]> _parents = new() {
        [typeof(AterraSingleton)] = [],
        [typeof(AterraHost)] = [typeof(AterraSingleton)],
        [typeof(AterraWorld)] = [typeof(AterraHost)],
        [typeof(AterraScene)] = [typeof(AterraWorld)]
    };
    private readonly Dictionary<Type, ServiceRegistration> _registrations = [];
    private readonly Dictionary<Type, List<ServiceRegistration>> _registrationSets = [];
    private readonly Dictionary<ServiceKey, ServiceRegistration> _keyedRegistrations = [];
    private readonly Dictionary<ServiceKey, List<ServiceRegistration>> _keyedRegistrationSets = [];
    private readonly Dictionary<Type, GeneratedServiceCollectionResolver> _collectionResolvers = [];
    private ServiceDiagnosticsOptions? _diagnostics;
    private bool _built;
    private string? _module;

    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    public ServiceCollection Add<T>(ServiceLifetime lifetime) where T : class
        => Add<T, T>(lifetime);

    /// <summary>Enables immutable diagnostics for the provider built from this collection.</summary>
    public ServiceCollection ConfigureDiagnostics(ServiceDiagnosticsOptions options) {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Sink);
        _diagnostics = options;
        return this;
    }

    /// <summary>Applies generated service registrations from the assembly containing <typeparamref name="TAssemblyMarker"/>.</summary>
    public ServiceCollection RegisterActivators<TAssemblyMarker>()
        => RegisterActivators(typeof(TAssemblyMarker).Assembly);

    /// <summary>Applies generated service registrations from <paramref name="assembly"/>.</summary>
    public ServiceCollection RegisterActivators(Assembly assembly) {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(assembly);
        GeneratedServiceRegistration.Apply(assembly, this);
        return this;
    }

    public ServiceCollection Add<TService, TImplementation>(ServiceLifetime lifetime) where TImplementation : class, TService
        => Add(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation)));

    /// <summary>Appends a registration to the ordered registrations for <typeparamref name="TService"/>.</summary>
    public ServiceCollection AddEnumerable<TService, TImplementation>(ServiceLifetime lifetime)
        where TImplementation : class, TService
    {
        AddGeneratedCollectionResolver<TService>(static (ref resolver) => resolver.GetAll<TService>());
        return AddEnumerable(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation)));
    }

    public ServiceCollection AddGeneratedCollectionResolver<T>(GeneratedServiceCollectionResolver resolver) {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(resolver);
        _collectionResolvers[typeof(T)] = resolver;
        return this;
    }

    public ServiceCollection Add(ServiceRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);

        ServiceRecord alteredRecord = record with { Module = record.Module ?? _module };
        var registration = new ServiceRegistration(alteredRecord);
        return Register(registration);
    }

    public ServiceCollection AddEnumerable(ServiceRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);
        return Append(new ServiceRegistration(record with { Module = record.Module ?? _module }));
    }

    /// <summary>Installs a generated (or explicitly authored) constructor recipe, not a service registration.</summary>
    public ServiceCollection AddActivator<T>(Func<IServiceResolver, T> create, params Type[] dependencies) where T : class {
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
    public ServiceCollection AddGeneratedActivator<T>(GeneratedServiceActivator create, params Type[] dependencies) where T : class {
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
    public ServiceCollection AddFactory<T>(ServiceLifetime lifetime, Func<IServiceResolver, T> factory) where T : class {
        ArgumentNullException.ThrowIfNull(factory);

        var record = new ServiceRecord(lifetime, typeof(T), typeof(T), _module);
        ServiceRegistration registration = ServiceRegistration.AsFactory(record, factory);

        return Register(registration);
    }

    public ServiceCollection AddEnumerableFactory<T>(ServiceLifetime lifetime, Func<IServiceResolver, T> factory) where T : class {
        ArgumentNullException.ThrowIfNull(factory);
        var record = new ServiceRecord(lifetime, typeof(T), typeof(T), _module);
        AddGeneratedCollectionResolver<T>(static (ref resolver) => resolver.GetAll<T>());
        return Append(ServiceRegistration.AsFactory(record, factory));
    }

    public ServiceCollection AddInstance<T>(T instance, ServiceInstanceOwnership ownership) where T : class {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

        var record = new ServiceRecord(ServiceLifetime.Singleton, typeof(T), instance.GetType(), _module);
        var registration = ServiceRegistration.AsInstance(record, instance, ownership);

        return Register(registration);
    }

    public ServiceCollection AddEnumerableInstance<T>(T instance, ServiceInstanceOwnership ownership) where T : class {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));
        var record = new ServiceRecord(ServiceLifetime.Singleton, typeof(T), instance.GetType(), _module);
        AddGeneratedCollectionResolver<T>(static (ref resolver) => resolver.GetAll<T>());
        return Append(ServiceRegistration.AsInstance(record, instance, ownership));
    }

    public ServiceCollection AddKeyed<TService, TImplementation, TKey>(ServiceLifetime lifetime, TKey key)
        where TImplementation : class, TService
        => RegisterKeyed(new ServiceRegistration(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation), _module), ServiceKey.Of<TService, TKey>(key)));

    public ServiceCollection AddKeyed<TService, TImplementation, TKey>(TKey key, ServiceLifetime lifetime)
        where TImplementation : class, TService => AddKeyed<TService, TImplementation, TKey>(lifetime, key);

    public ServiceCollection AddKeyed<TService, TImplementation>(ServiceLifetime lifetime, object? key)
        where TImplementation : class, TService => RegisterKeyed(new ServiceRegistration(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation), _module), ServiceKey.OfRuntime<TService>(key)));

    public ServiceCollection AddKeyed<TService, TImplementation>(object? key, ServiceLifetime lifetime)
        where TImplementation : class, TService => AddKeyed<TService, TImplementation>(lifetime, key);

    public ServiceCollection AddNamed<TService, TImplementation>(string name, ServiceLifetime lifetime)
        where TImplementation : class, TService => AddKeyed<TService, TImplementation, string>(lifetime, name);

    public ServiceCollection AddKeyedEnumerable<TService, TImplementation, TKey>(ServiceLifetime lifetime, TKey key)
        where TImplementation : class, TService {
        return AppendKeyed(new ServiceRegistration(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation), _module), ServiceKey.Of<TService, TKey>(key)));
    }

    public ServiceCollection AddKeyedEnumerable<TService, TImplementation>(ServiceLifetime lifetime, object? key)
        where TImplementation : class, TService => AppendKeyed(new ServiceRegistration(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation), _module), ServiceKey.OfRuntime<TService>(key)));

    public ServiceCollection AddNamedEnumerable<TService, TImplementation>(string name, ServiceLifetime lifetime)
        where TImplementation : class, TService => AddKeyedEnumerable<TService, TImplementation, string>(lifetime, name);

    public ServiceCollection AddKeyedFactory<TService, TKey>(ServiceLifetime lifetime, TKey key, Func<IServiceResolver, TService> factory)
        where TService : class {
        ArgumentNullException.ThrowIfNull(factory);
        return RegisterKeyed(ServiceRegistration.AsFactory(new ServiceRecord(lifetime, typeof(TService), typeof(TService), _module), factory, ServiceKey.Of<TService, TKey>(key)));
    }

    public ServiceCollection AddKeyedFactory<TService, TKey>(TKey key, ServiceLifetime lifetime, Func<IServiceResolver, TService> factory)
        where TService : class => AddKeyedFactory(lifetime, key, factory);

    public ServiceCollection AddNamedFactory<TService>(string name, ServiceLifetime lifetime, Func<IServiceResolver, TService> factory)
        where TService : class => AddKeyedFactory(lifetime, name, factory);

    public ServiceCollection AddKeyedEnumerableFactory<TService, TKey>(ServiceLifetime lifetime, TKey key, Func<IServiceResolver, TService> factory)
        where TService : class {
        ArgumentNullException.ThrowIfNull(factory);
        return AppendKeyed(ServiceRegistration.AsFactory(new ServiceRecord(lifetime, typeof(TService), typeof(TService), _module), factory, ServiceKey.Of<TService, TKey>(key)));
    }

    public ServiceCollection AddKeyedInstance<TService, TKey>(TKey key, TService instance, ServiceInstanceOwnership ownership)
        where TService : class {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));
        return RegisterKeyed(ServiceRegistration.AsInstance(new ServiceRecord(ServiceLifetime.Singleton, typeof(TService), instance.GetType(), _module), instance, ownership, ServiceKey.Of<TService, TKey>(key)));
    }

    public ServiceCollection AddKeyedEnumerableInstance<TService, TKey>(TKey key, TService instance, ServiceInstanceOwnership ownership)
        where TService : class {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));
        return AppendKeyed(ServiceRegistration.AsInstance(new ServiceRecord(ServiceLifetime.Singleton, typeof(TService), instance.GetType(), _module), instance, ownership, ServiceKey.Of<TService, TKey>(key)));
    }

    public ServiceCollection AddNamedInstance<TService>(string name, TService instance, ServiceInstanceOwnership ownership)
        where TService : class => AddKeyedInstance(name, instance, ownership);

    /// <summary>Wraps every current unkeyed registration of <typeparamref name="TService"/>.</summary>
    public ServiceCollection Decorate<TService, TDecorator>(Func<TService, TDecorator> decorator)
        where TService : class where TDecorator : class, TService {
        ArgumentNullException.ThrowIfNull(decorator);
        return DecorateCore(typeof(TService), null, typeof(TDecorator), inner => decorator((TService)inner), null);
    }

    /// <summary>Wraps every current unkeyed registration using a generated constructor activator.</summary>
    public ServiceCollection Decorate<TService, TDecorator>()
        where TService : class where TDecorator : class, TService
        => DecorateGeneratedCore<TService, TDecorator>(null);

    /// <summary>Wraps all registrations for one exact keyed service identity.</summary>
    public ServiceCollection Decorate<TService, TDecorator, TKey>(TKey key, Func<TService, TDecorator> decorator)
        where TService : class where TDecorator : class, TService {
        ArgumentNullException.ThrowIfNull(decorator);
        return DecorateCore(typeof(TService), ServiceKey.Of<TService, TKey>(key), typeof(TDecorator), inner => decorator((TService)inner), null);
    }

    /// <summary>Wraps one exact keyed service identity using a generated constructor activator.</summary>
    public ServiceCollection Decorate<TService, TDecorator, TKey>(TKey key)
        where TService : class where TDecorator : class, TService
        => DecorateGeneratedCore<TService, TDecorator>(ServiceKey.Of<TService, TKey>(key));

    /// <summary>Registers a generated decorator constructor explicitly, avoiding runtime reflection.</summary>
    public ServiceCollection Decorate<TService, TDecorator>(GeneratedServiceActivator create, params Type[] dependencies)
        where TService : class where TDecorator : class, TService {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(dependencies);
        return DecorateCore(typeof(TService), null, typeof(TDecorator), null,
            new ServiceActivationPlan(null, create, dependencies.ToArray()));
    }

    private ServiceCollection DecorateGeneratedCore<TService, TDecorator>(ServiceKey? key)
        where TService : class where TDecorator : class, TService {
        if (!_activators.TryGetValue(typeof(TDecorator), out ServiceActivationPlan? activator) || activator.GeneratedCreate is null)
            throw new DependencyInjectionException($"No generated constructor activator is registered for decorator {typeof(TDecorator)}. Register its generated activator or use the factory overload.");
        if (activator.Dependencies.Contains(typeof(TService)))
            throw new DependencyInjectionException($"Generated decorator {typeof(TDecorator)} requests {typeof(TService)} as a public dependency. Mark the constructor parameter as a decorated dependency or use GetInner<TService>().");
        return DecorateCore(typeof(TService), key, typeof(TDecorator), null, activator);
    }

    private ServiceCollection DecorateCore(Type service, ServiceKey? key, Type decorator, Func<object, object>? factory, ServiceActivationPlan? activator) {
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

    private static ServiceRegistration Wrap(ServiceRegistration inner, Type decorator, Func<object, object>? factory, ServiceActivationPlan? activator) {
        if (!decorator.IsAssignableTo(inner.Record.Service))
            throw new DependencyInjectionException($"Decorator {decorator} is not assignable to service {inner.Record.Service}.");
        return ServiceRegistration.AsDecorator(inner.Record with { Implementation = decorator }, inner, factory, activator, inner.Key);
    }

    public ServiceCollection AddModule(string name, Action<ServiceCollection> configure) {
        ThrowIfNotMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        string? previous = _module;
        _module = name;
        try {
            configure(this);
        }
        finally {
            // Reset if failed
            _module = previous;
        }

        return this;
    }

    public ServiceCollection DeclareScope<TScope>(params Type[] allowedParents) {
        ThrowIfNotMutable();
        ArgumentNullException.ThrowIfNull(allowedParents);
        ArgumentNullException.ThrowIfContainsAnyNull<Type[], Type>(allowedParents);

        return _parents.TryAdd(typeof(TScope), allowedParents.ToArray())
            ? this
            : throw new DependencyInjectionException($"Scope {typeof(TScope)} is already declared.");
    }

    public ServiceCollection RequireInput<TScope, TInput>() where TInput : notnull {
        ThrowIfNotMutable();

        if (ServiceProvider.IsProviderService(typeof(TInput)))
            throw new DependencyInjectionException($"Input {typeof(TInput)} conflicts with the built-in provider service.");
        if (_registrations.ContainsKey(typeof(TInput)) || !_inputs.TryAdd(typeof(TInput), typeof(TScope)))
            throw new DependencyInjectionException($"Input {typeof(TInput)} conflicts with an existing service or input.");

        return this;
    }

    public ServiceProvider Build(params ServiceScopeInput[] hostInputs) {
        ThrowIfNotMutable();
        if (_module is not null) throw new DependencyInjectionException("Build cannot run inside a module contribution.");

        ServiceCollectionValidator.Validate(_activators, _inputs, _parents, _registrations, _registrationSets,
            _keyedRegistrations.Values, _keyedRegistrationSets.Values.SelectMany(static values => values));

        var registrations = new Dictionary<Type, ServiceRegistration>(_registrations);
        var registrationSets = _registrationSets.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var keyedRegistrations = new Dictionary<ServiceKey, ServiceRegistration>(_keyedRegistrations);
        var keyedRegistrationSets = _keyedRegistrationSets.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var collectionResolvers = new Dictionary<Type, GeneratedServiceCollectionResolver>(_collectionResolvers);
        Dictionary<Type, Type[]> parents = _parents.ToDictionary(
            keySelector: p => p.Key,
            elementSelector: p => p.Value.ToArray()
        );
        var inputOwners = new Dictionary<Type, Type>(_inputs);

        var provider = new ServiceProvider(registrations, registrationSets, keyedRegistrations, keyedRegistrationSets, collectionResolvers, parents, inputOwners, hostInputs, _diagnostics);
        _built = true;

        return provider;
    }

    private ServiceCollection Register(ServiceRegistration registration) {
        ThrowIfNotMutable();
        Type service = registration.Record.Service;
        if (ServiceProvider.IsProviderService(service))
            throw registration.Error("Conflicts with the built-in provider service.");
        if (_inputs.ContainsKey(service)) throw registration.Error("Conflicts with a declared input.");
        _registrations[service] = registration;
        _registrationSets[service] = [registration];
        return this;
    }

    private ServiceCollection Append(ServiceRegistration registration) {
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

    private ServiceCollection RegisterKeyed(ServiceRegistration registration) {
        ThrowIfNotMutable();
        Type service = registration.Record.Service;
        if (ServiceProvider.IsProviderService(service)) throw registration.Error("Conflicts with the built-in provider service.");
        if (_inputs.ContainsKey(service)) throw registration.Error("Conflicts with a declared input.");
        _keyedRegistrations[registration.Key!.Value] = registration;
        _keyedRegistrationSets[registration.Key.Value] = [registration];
        return this;
    }

    private ServiceCollection AppendKeyed(ServiceRegistration registration) {
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
