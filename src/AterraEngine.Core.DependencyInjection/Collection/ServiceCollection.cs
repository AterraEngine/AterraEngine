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
    private bool _built;
    private string? _module;

    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    public ServiceCollection Add<T>(ServiceLifetime lifetime) where T : class
        => Add<T, T>(lifetime);

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

    public ServiceCollection Add(ServiceRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Service);
        ArgumentNullException.ThrowIfNull(record.Implementation);

        ServiceRecord alteredRecord = record with { Module = record.Module ?? _module };
        var registration = new ServiceRegistration(alteredRecord);
        return Register(registration);
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

    public ServiceCollection AddInstance<T>(T instance, ServiceInstanceOwnership ownership) where T : class {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

        var record = new ServiceRecord(ServiceLifetime.Singleton, typeof(T), instance.GetType(), _module);
        var registration = ServiceRegistration.AsInstance(record, instance, ownership);

        return Register(registration);
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

        ServiceCollectionValidator.Validate(_activators, _inputs, _parents, _registrations);

        var registrations = new Dictionary<Type, ServiceRegistration>(_registrations);
        Dictionary<Type, Type[]> parents = _parents.ToDictionary(
            keySelector: p => p.Key,
            elementSelector: p => p.Value.ToArray()
        );
        var inputOwners = new Dictionary<Type, Type>(_inputs);

        var provider = new ServiceProvider(registrations, parents, inputOwners, hostInputs);
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
        return this;
    }

    private void ThrowIfNotMutable() {
        if (_built) throw new InvalidOperationException("Configuration is immutable after Build.");
    }

}
