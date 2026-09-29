// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Runtime.ExceptionServices;
using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>An engine singleton root with a primary Host scope. Shutdown and failed-activation cleanup are asynchronous.</summary>
public sealed class ServiceProvider : IAsyncDisposable, IServiceProvider {
    [ThreadStatic]
    private static Dictionary<ServiceProvider, int>? _threadActivations;
    [ThreadStatic]
    private static ServiceResolutionContext? _spareContexts;
    private readonly Dictionary<object, long> _claimed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> _external = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, ServiceRegistration> _registrations;
    private long _constructionOrder;

    internal Lock Gate { get; } = new();
    internal Dictionary<Type, Type[]> Parents { get; }
    private Dictionary<Type, Type> InputOwners { get; }
    public OwnedScope Singleton { get; }
    public OwnedScope Host { get; }

    // -----------------------------------------------------------------------------------------------------------------
    // Constructors
    // -----------------------------------------------------------------------------------------------------------------
    internal ServiceProvider(
        Dictionary<Type, ServiceRegistration> registrations,
        Dictionary<Type, Type[]> parents,
        Dictionary<Type, Type> inputOwners,
        ScopeInput[] inputs
    ) {
        _registrations = registrations;
        Parents = parents;
        InputOwners = inputOwners;
        ArgumentNullException.ThrowIfNull(inputs);
        foreach (ScopeInput input in inputs) {
            ArgumentNullException.ThrowIfNull(input);
            if (!InputOwners.TryGetValue(input.Type, out Type? owner) || owner != typeof(Singleton) && owner != typeof(Host))
                throw new DependencyInjectionException($"Input {input.Type} is not declared for Singleton or Host.");
        }

        ScopeInput[] singletonInputs = inputs.Where(input => InputOwners[input.Type] == typeof(Singleton)).ToArray();
        ScopeInput[] hostInputs = inputs.Where(input => InputOwners[input.Type] == typeof(Host)).ToArray();
        Singleton = new OwnedScope(this, typeof(Singleton), null, ValidateInputs(typeof(Singleton), singletonInputs));
        Host = Singleton.CreateScope<Host>(hostInputs);
        foreach (ServiceRegistration registration in registrations.Values) {
            if (registration.Instance is {} instance && inputs.Any(input => ReferenceEquals(input.Value, instance)))
                throw registration.Error("An external service cannot also be registered as a scope input.");
        }

        foreach (ServiceRegistration registration in registrations.Values) {
            if (registration.Instance is not {} instance) continue;

            _claimed.Add(instance, ++_constructionOrder);
            if (registration.Ownership == ServiceInstanceOwnership.Container && IsDisposable(instance)) Singleton.Owned.Add(instance);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    public ValueTask DisposeAsync() => Singleton.DisposeAsync();

    public ValueTask<T> ResolveAsync<T>() where T : notnull => Host.ResolveAsync<T>();
    public OwnedScope CreateScope<TScope>(params ScopeInput[] inputs) => Host.CreateScope<TScope>(inputs);
    public object? GetService(Type serviceType) {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (!IsProviderService(serviceType) && !InputOwners.ContainsKey(serviceType) && !_registrations.ContainsKey(serviceType)) return null;

        return Host.ResolveAsync(serviceType).GetAwaiter().GetResult();
    }

    internal Dictionary<Type, object> ValidateInputs(Type scope, ScopeInput[] inputs) {
        ArgumentNullException.ThrowIfNull(inputs);
        var values = new Dictionary<Type, object>();
        foreach (ScopeInput input in inputs) {
            ArgumentNullException.ThrowIfNull(input);
            if (!InputOwners.TryGetValue(input.Type, out Type? owner) || owner != scope)
                throw new DependencyInjectionException($"Input {input.Type} is not declared for {scope}.");
            if (!values.TryAdd(input.Type, input.Value)) throw new DependencyInjectionException($"Duplicate input {input.Type}.");
        }

        foreach ((Type input, Type owner) in InputOwners) {
            if (owner == scope && !values.ContainsKey(input)) throw new DependencyInjectionException($"Required input {input} is missing for {scope}.");
        }

        return values;
    }

    internal ValueTask<object> ResolveAsync(OwnedScope scope, Type service) {
        ArgumentNullException.ThrowIfNull(service);
        scope.Enter();
        try {
            if (TryResolveWithoutActivation(scope, service, out object cached)) {
                scope.Exit();
                return new ValueTask<object>(cached);
            }

            ServiceResolutionContext context = RentContext();
            ServiceCacheEntry? faultedEntry = null;
            try {
                object? result = Resolve(service, context, scope, null, true, out Task<ServiceOutcome>? pending, out faultedEntry);
                if (pending is not null) {
                    ReturnContext(context);
                    return AwaitCachedResolutionAsync(scope, pending);
                }

                context.CommitResources(scope, 0);
                ReturnContext(context);
                scope.Exit();
                return new ValueTask<object>(result!);
            }
            catch (Exception exception) {
                context.FailResources(0);
                if (context.Failed.Count == 0) {
                    faultedEntry?.Completion.SetResult(new ServiceFailure(exception));
                    ReturnContext(context);
                    scope.Exit();
                    return ValueTask.FromException<object>(exception);
                }

                List<object> failed = context.TakeFailed();
                ReturnContext(context);
                return CompleteFailedResolutionAsync(scope, failed, exception, faultedEntry);
            }
        }
        catch (Exception exception) {
            scope.Exit();
            return ValueTask.FromException<object>(exception);
        }
    }

    internal object ResolveGenerated(Type service, ServiceResolutionContext context, OwnedScope anchor, ServiceCacheEntry? cacheEntry)
        => Resolve(service, context, anchor, cacheEntry);

    internal object Resolve(
        Type service,
        ServiceResolutionContext context,
        OwnedScope callerAnchor,
        ServiceCacheEntry? callerEntry
    ) => Resolve(service, context, callerAnchor, callerEntry, false, out _, out _)!;

    private object? Resolve(
        Type service,
        ServiceResolutionContext context,
        OwnedScope callerAnchor,
        ServiceCacheEntry? callerEntry,
        bool deferCachedWait,
        out Task<ServiceOutcome>? pending,
        out ServiceCacheEntry? faultedEntry
    ) {
        pending = null;
        faultedEntry = null;
        if (IsProviderService(service)) return ResolveProviderService(callerAnchor, service);

        if (InputOwners.TryGetValue(service, out Type? inputOwner)) {
            OwnedScope owner = FindInputOwner(callerAnchor, inputOwner, service);
            return owner.Inputs[service];
        }

        if (!_registrations.TryGetValue(service, out ServiceRegistration? registration))
            throw new DependencyInjectionException($"Unregistered service {service}; path: {context.PathText}.");
        if (context.Path.Contains(registration)) throw registration.Error($"Dependency cycle: {context.PathText} -> {registration.Label}.");

        Type? scopeType = registration.Record.Lifetime.ScopeType;
        OwnedScope anchor = scopeType is null ? callerAnchor : FindServiceOwner(callerAnchor, scopeType, registration);
        if (registration.Instance is {} instance) return instance;
        if (scopeType is null) return Activate(registration, anchor, callerEntry, context, false);

        ServiceCacheEntry entry;
        bool construct;
        lock (Gate) {
            construct = !anchor.Cache.TryGetValue(service, out entry!);
            if (construct) anchor.Cache.TryAdd(service, entry = new ServiceCacheEntry(registration.Label));
            if (callerEntry is {} parent && !entry.Completion.Task.IsCompleted) {
                if (Reaches(entry, parent, [])) throw registration.Error($"Concurrent dependency cycle between {parent.Label} and {entry.Label}.");

                parent.Dependencies.Add(entry);
            }
        }

        try {
            if (construct) {
                ServiceOutcome outcome;
                try { outcome = Activate(registration, anchor, entry, context, true); }
                catch (Exception exception) {
                    if (deferCachedWait) {
                        faultedEntry = entry;
                        throw;
                    }

                    outcome = new ServiceFailure(exception);
                }

                entry.Completion.SetResult(outcome);
            }
            else if (deferCachedWait) {
                pending = entry.Completion.Task;
                return null;
            }

            // Nested dependencies are requested from synchronous constructors and factories, so they cannot suspend.
            // Top-level ResolveAsync callers defer this wait and await the entry in AwaitCachedResolutionAsync instead.
            ServiceOutcome completed = entry.Completion.Task.GetAwaiter().GetResult();
            return GetServiceValue(completed);
        }
        finally {
            lock (Gate) {
                callerEntry?.Dependencies.Remove(entry);
            }
        }
    }

    private object Activate(
        ServiceRegistration registration,
        OwnedScope anchor,
        ServiceCacheEntry? cacheEntry,
        ServiceResolutionContext context,
        bool cached
    ) {
        int resourceStart = context.ResourceCount;
        context.Path.Add(registration);
        FactoryResolver? resolver = null;
        Dictionary<ServiceProvider, int> activations = _threadActivations ??= [];
        activations[this] = activations.GetValueOrDefault(this) + 1;

        try {
            object value;
            if (registration.Activator?.GeneratedCreate is {} generated) {
                var generatedResolver = new GeneratedServiceResolver(this, context, anchor, cacheEntry);
                value = generated(ref generatedResolver);
            }
            else {
                resolver = new FactoryResolver(this, context, anchor, cacheEntry);
                value = (registration.Factory ?? registration.Activator!.Create!)(resolver);
            }

            if (value is null || !registration.Record.Service.IsInstanceOfType(value)) throw registration.Error("Factory returned null or an incompatible object.");

            if (IsDisposable(value)) {
                lock (Gate) {
                    if (_external.ContainsKey(value) || !_claimed.TryAdd(value, ++_constructionOrder))
                        throw registration.Error("Factory returned an object already owned or registered externally. Return a new instance.");
                }

                context.AddResource(value);
            }

            if (cached) context.CommitResources(anchor, resourceStart);
            return value;
        }
        catch (Exception exception) {
            context.FailResources(resourceStart);
            if (exception is DependencyInjectionException) throw;

            throw registration.Error("Activation failed.", exception);
        }
        finally {
            resolver?.Close();
            context.Path.RemoveAt(context.Path.Count - 1);
            if (activations[this] == 1) activations.Remove(this);
            else activations[this]--;
        }
    }

    private ServiceResolutionContext RentContext() {
        ServiceResolutionContext? context = _spareContexts;
        if (context is null) return new ServiceResolutionContext(this);

        _spareContexts = context.Next;
        context.Reset(this);
        return context;
    }

    private static void ReturnContext(ServiceResolutionContext context) {
        context.Reset(null);
        context.Next = _spareContexts;
        _spareContexts = context;
    }

    private async ValueTask<object> CompleteFailedResolutionAsync(
        OwnedScope scope,
        List<object> failed,
        Exception error,
        ServiceCacheEntry? faultedEntry
    ) {
        try {
            List<Exception> failures = await CleanupAsync(failed).ConfigureAwait(false);
            if (failures.Count != 0) {
                failures.Insert(0, error);
                error = new AggregateException("Activation cleanup failed.", failures);
            }

            faultedEntry?.Completion.SetResult(new ServiceFailure(error));
            ExceptionDispatchInfo.Capture(error).Throw();
            return null!;
        }
        finally { scope.Exit(); }
    }

    private static async ValueTask<object> AwaitCachedResolutionAsync(OwnedScope scope, Task<ServiceOutcome> pending) {
        try {
            return GetServiceValue(await pending.ConfigureAwait(false));
        }
        finally { scope.Exit(); }
    }

    private bool TryResolveWithoutActivation(OwnedScope scope, Type service, out object value) {
        if (IsProviderService(service)) {
            value = ResolveProviderService(scope, service);
            return true;
        }

        if (InputOwners.TryGetValue(service, out Type? inputOwner)) {
            value = FindInputOwner(scope, inputOwner, service).Inputs[service];
            return true;
        }

        if (!_registrations.TryGetValue(service, out ServiceRegistration? registration)) {
            value = null!;
            return false;
        }

        if (registration.Instance is {} instance) {
            value = instance;
            return true;
        }

        if (registration.Record.Lifetime.ScopeType is not {} scopeType) {
            value = null!;
            return false;
        }

        OwnedScope anchor = FindServiceOwner(scope, scopeType, registration);
        anchor.Cache.TryGetValue(service, out ServiceCacheEntry? entry);

        if (entry is null || !entry.Completion.Task.IsCompletedSuccessfully) {
            value = null!;
            return false;
        }

        ServiceOutcome outcome = entry.Completion.Task.GetAwaiter().GetResult();
        value = GetServiceValue(outcome);
        return true;
    }

    private static object GetServiceValue(ServiceOutcome outcome) {
        if (outcome.Value is ServiceFailure failure) ExceptionDispatchInfo.Capture(failure.Error).Throw();
        return outcome.Value ?? throw new InvalidOperationException("Unexpected null service outcome.");
    }

    private static OwnedScope? FindOwner(OwnedScope from, Type type) {
        for (OwnedScope? scope = from; scope is not null; scope = scope.Parent) {
            if (scope.ScopeType == type) return scope;
        }

        return null;
    }

    private static OwnedScope FindInputOwner(OwnedScope from, Type type, Type input)
        => FindOwner(from, type) ?? throw new DependencyInjectionException(
            $"Missing ownership scope {type.Name} for input {input}, resolving from {from.ScopeType.Name}. Descendants and siblings are not visible."
        );

    private static OwnedScope FindServiceOwner(OwnedScope from, Type type, ServiceRegistration registration)
        => FindOwner(from, type) ?? throw new DependencyInjectionException(
            $"Missing ownership scope {type.Name} for {registration.Label}, resolving from {from.ScopeType.Name}. Descendants and siblings are not visible."
        );

    private object ResolveProviderService(OwnedScope from, Type service) {
        if (FindOwner(from, typeof(Host)) is null)
            throw new DependencyInjectionException($"Missing ownership scope Host for provider service {service}, resolving from {from.ScopeType.Name}.");

        return this;
    }

    private static bool Reaches(ServiceCacheEntry from, ServiceCacheEntry target, HashSet<ServiceCacheEntry> visited) =>
        from == target || visited.Add(from) && from.Dependencies.Any(next => Reaches(next, target, visited));

    private static bool IsDisposable(object instance) => instance is IDisposable or IAsyncDisposable;
    internal static bool IsProviderService(Type service) => service == typeof(IServiceProvider) || service == typeof(ServiceProvider);

    internal async Task<List<Exception>> CleanupAsync(List<object> instances) {
        var errors = new List<Exception>();
        // Nested failures and concurrent activations can reach this list in a different order
        // from successful construction. The ownership claim is the linearization point.
        lock (Gate) {
            instances.Sort((left, right) => _claimed[left].CompareTo(_claimed[right]));
        }

        for (int index = instances.Count - 1; index >= 0; index--) {
            object instance = instances[index];
            try {
                if (instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (instance is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception exception) { errors.Add(exception); }
            finally {
                lock (Gate) {
                    _claimed.Remove(instance);
                }
            }
        }

        instances.Clear();
        return errors;
    }

    internal void ReleaseProvider() {
        _claimed.Clear();
        _external.Clear();
        _registrations.Clear();
    }

    internal void RejectReentrantResolution() {
        if (_threadActivations?.ContainsKey(this) == true)
            throw new DependencyInjectionException("Reentrant public resolution during activation is prohibited; factories must use their supplied IServiceResolver to preserve cycle and ownership checks.");
    }

    internal void TrackInputs(IReadOnlyCollection<object> inputs) {
        foreach (object input in inputs) {
            if (_claimed.ContainsKey(input)) throw new DependencyInjectionException("A container-owned service cannot also be a caller-owned scope input.");
        }

        foreach (object input in inputs) _external[input] = _external.GetValueOrDefault(input) + 1;
    }

    internal void ReleaseInputs(IEnumerable<object> inputs) {
        foreach (object input in inputs) {
            if (--_external[input] == 0) _external.Remove(input);
        }
    }
}
