// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection.Collection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Single-threaded configuration, frozen after a successful Build.</summary>
public sealed class ServiceCollection {
    private readonly Dictionary<Type, ServiceActivationPlan> _activators = [];
    private readonly Dictionary<Type, Type> _inputs = [];
    private readonly Dictionary<Type, Type[]> _parents = new() {
        [typeof(Host)] = [], [typeof(World)] = [typeof(Host)], [typeof(Scene)] = [typeof(World)]
    };
    private readonly Dictionary<Type, ServiceRegistration> _registrations = [];
    private bool _built;
    private string? _module;

    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    public ServiceCollection Add<T>(Lifetime lifetime) where T : class
        => Add<T, T>(lifetime);

    public ServiceCollection Add<TService, TImplementation>(Lifetime lifetime) where TImplementation : class, TService
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

        var activator = new ServiceActivationPlan(create, dependencies.ToArray());
        return _activators.TryAdd(typeof(T), activator)
            ? this
            : throw new DependencyInjectionException($"An activator for {typeof(T)} is already installed.");
    }

    /// <summary>Opaque factory dependencies are checked at runtime, not during Build.</summary>
    public ServiceCollection AddFactory<T>(Lifetime lifetime, Func<IServiceResolver, T> factory) where T : class {
        ArgumentNullException.ThrowIfNull(factory);

        var record = new ServiceRecord(lifetime, typeof(T), typeof(T), _module);
        ServiceRegistration registration = ServiceRegistration.AsFactory(record, factory);

        return Register(registration);
    }

    public ServiceCollection AddInstance<T>(T instance, InstanceOwnership ownership) where T : class {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

        var record = new ServiceRecord(Lifetime.Host, typeof(T), instance.GetType(), _module);
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

        if (_registrations.ContainsKey(typeof(TInput)) || !_inputs.TryAdd(typeof(TInput), typeof(TScope)))
            throw new DependencyInjectionException($"Input {typeof(TInput)} conflicts with an existing service or input.");

        return this;
    }

    public ServiceProvider Build(params ScopeInput[] hostInputs) {
        ThrowIfNotMutable();
        if (_module is not null) throw new DependencyInjectionException("Build cannot run inside a module contribution.");

        Validate();

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
        if (_inputs.ContainsKey(service)) throw registration.Error("Conflicts with a declared input.");
        if (_registrations.TryGetValue(service, out ServiceRegistration? existing))
            throw registration.Error($"Duplicate registration; previous contributor: {existing.Label}.");

        _registrations.Add(service, registration);
        return this;
    }

    private void ThrowIfNotMutable() {
        if (_built) throw new InvalidOperationException("Configuration is immutable after Build.");
    }

    private void Validate() {
        var guaranteedAncestors = new Dictionary<Type, HashSet<Type>>();

        foreach (Type scope in _parents.Keys) Ancestors(scope, []);
        foreach ((Type input, Type scope) in _inputs) {
            if (!_parents.ContainsKey(scope) || input.ContainsGenericParameters || input == typeof(void))
                throw new DependencyInjectionException($"Invalid input declaration {input} for {scope}.");
        }

        var externalObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (ServiceRegistration registration in _registrations.Values) {
            (Lifetime lifetime, Type service, Type implementation, _) = registration.Record;
            if (service.ContainsGenericParameters || service == typeof(void) || service.IsByRef || service.IsPointer)
                throw registration.Error("Service must be a closed, resolvable type.");
            if (lifetime.ScopeType is {} scope && !_parents.ContainsKey(scope)) throw registration.Error($"Undeclared lifetime scope {scope}.");

            if (registration.Instance is {} instance) {
                if (!externalObjects.Add(instance)) throw registration.Error("The same external object cannot be registered twice.");

                continue;
            }

            if (registration.Factory is not null) continue;

            if (!implementation.IsClass || implementation.IsAbstract || implementation.ContainsGenericParameters || !service.IsAssignableFrom(implementation))
                throw registration.Error($"Invalid implementation {implementation}.");
            if (!_activators.TryGetValue(implementation, out ServiceActivationPlan? activator))
                throw registration.Error($"No generated activator for {implementation}. Install the module's AddActivators output or register an explicit factory.");

            registration.Activator = activator;
        }

        var validated = new HashSet<(Type Service, Type? Anchor)>();

        foreach (Type service in _registrations.Keys) Visit(service, null, []);
        return;

        void Visit(Type service, Type? anchor, List<Type> path) {
            if (_inputs.TryGetValue(service, out Type? inputScope)) {
                if (anchor is not null && !guaranteedAncestors[anchor].Contains(inputScope))
                    throw new DependencyInjectionException($"Lifetime violation: {string.Join(" -> ", path.Select(t => _registrations[t].Label))} -> input {service} requires {inputScope} from {anchor}.");

                return;
            }

            if (!_registrations.TryGetValue(service, out ServiceRegistration? registration))
                throw new DependencyInjectionException($"Missing dependency {service}; path: {string.Join(" -> ", path.Select(t => _registrations[t].Label))}.");
            if (path.Contains(service)) throw registration.Error($"Dependency cycle: {string.Join(" -> ", path.Append(service).Select(t => t.Name))}.");

            Type? owner = registration.Record.Lifetime.ScopeType;
            if (anchor is not null && owner is not null && !guaranteedAncestors[anchor].Contains(owner))
                throw registration.Error($"Lifetime violation from {anchor}: {string.Join(" -> ", path.Select(t => _registrations[t].Label))} -> {registration.Label} requires {owner}.");

            if (validated.Contains((service, anchor))) return;

            path.Add(service);
            foreach (Type dependency in registration.Activator?.Dependencies ?? []) Visit(dependency, owner ?? anchor, path);
            path.RemoveAt(path.Count - 1);
            validated.Add((service, anchor));
        }

        HashSet<Type> Ancestors(Type scope, HashSet<Type> visiting) {
            if (guaranteedAncestors.TryGetValue(scope, out HashSet<Type>? result)) return result;

            if (!_parents.TryGetValue(scope, out Type[]? parents)) throw new DependencyInjectionException($"Undeclared scope {scope}.");
            if (!visiting.Add(scope)) throw new DependencyInjectionException($"Scope parent cycle involving {scope}.");
            if (scope.ContainsGenericParameters || scope == typeof(void) || scope != typeof(Host) && parents.Length == 0)
                throw new DependencyInjectionException($"Invalid scope {scope}: it must have a path to Host.");

            HashSet<Type>? common = null;
            foreach (Type parent in parents) {
                HashSet<Type> ancestors = Ancestors(parent, visiting);
                if (common is null) common = new HashSet<Type>(ancestors);
                else common.IntersectWith(ancestors);
            }

            result = common ?? [];
            result.Add(scope);
            visiting.Remove(scope);
            guaranteedAncestors.Add(scope, result);
            return result;
        }
    }
}
