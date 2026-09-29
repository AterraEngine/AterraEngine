using System.Runtime.ExceptionServices;
using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection;
/// <summary>An engine singleton root with a primary Host scope. Shutdown and failed-activation cleanup are asynchronous.</summary>
public sealed class ServiceProvider : IAsyncDisposable, IServiceProvider {
    [ThreadStatic]
    private static Dictionary<ServiceProvider, int>? _threadActivations;
    [ThreadStatic]
    private static ResolutionContext? _spareContexts;
    private readonly Dictionary<object, long> _claimed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> _external = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, ServiceRegistration> _registrations;
    private long _constructionOrder;

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
            if (registration.Ownership == InstanceOwnership.Container && IsDisposable(instance)) Singleton.Owned.Add(instance);
        }
    }
    internal object Gate { get; } = new();
    internal Dictionary<Type, Type[]> Parents { get; }
    internal Dictionary<Type, Type> InputOwners { get; }
    public OwnedScope Singleton { get; }
    public OwnedScope Host { get; }
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

            ResolutionContext context = RentContext();
            try {
                object result = Resolve(service, context, scope, null);
                context.CommitResources(scope, 0);
                ReturnContext(context);
                scope.Exit();
                return new ValueTask<object>(result);
            }
            catch (Exception exception) {
                context.FailResources(0);
                if (context.Failed.Count == 0) {
                    ReturnContext(context);
                    scope.Exit();
                    return ValueTask.FromException<object>(exception);
                }

                List<object> failed = context.TakeFailed();
                ReturnContext(context);
                return CompleteFailedResolutionAsync(scope, failed, exception);
            }
        }
        catch (Exception exception) {
            scope.Exit();
            return ValueTask.FromException<object>(exception);
        }
    }

    internal object ResolveGenerated(Type service, ResolutionContext context, OwnedScope anchor, CacheSlot? slot)
        => Resolve(service, context, anchor, slot);

    private object Resolve(
        Type service,
        ResolutionContext context,
        OwnedScope callerAnchor,
        CacheSlot? callerSlot
    ) {
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
        if (scopeType is null) return Activate(registration, anchor, callerSlot, context, false);

        CacheSlot slot;
        bool construct;
        lock (Gate) {
            construct = !anchor.Cache.TryGetValue(service, out slot!);
            if (construct) anchor.Cache.TryAdd(service, slot = new CacheSlot(registration.Label));
            if (callerSlot is {} parent && !slot.Completion.Task.IsCompleted) {
                if (Reaches(slot, parent, [])) throw registration.Error($"Concurrent dependency cycle between {parent.Label} and {slot.Label}.");

                parent.Dependencies.Add(slot);
            }
        }

        try {
            if (construct) {
                Outcome outcome;
                try { outcome = new Outcome(Activate(registration, anchor, slot, context, true), null); }
                catch (Exception exception) { outcome = new Outcome(null, exception); }

                slot.Completion.SetResult(outcome);
            }

            // Only synchronous construction is waited on here. Async cleanup is always awaited above.
            Outcome completed = slot.Completion.Task.GetAwaiter().GetResult();
            if (completed.Error is not null) ExceptionDispatchInfo.Capture(completed.Error).Throw();
            return completed.Value!;
        }
        finally {
            lock (Gate) {
                callerSlot?.Dependencies.Remove(slot);
            }
        }
    }

    private object Activate(
        ServiceRegistration registration,
        OwnedScope anchor,
        CacheSlot? slot,
        ResolutionContext context,
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
                var generatedResolver = new GeneratedServiceResolver(this, context, anchor, slot);
                value = generated(ref generatedResolver);
            }
            else {
                resolver = new FactoryResolver(this, context, anchor, slot);
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

    private ResolutionContext RentContext() {
        ResolutionContext? context = _spareContexts;
        if (context is null) return new ResolutionContext(this);

        _spareContexts = context.Next;
        context.Reset(this);
        return context;
    }

    private static void ReturnContext(ResolutionContext context) {
        context.Reset(null);
        context.Next = _spareContexts;
        _spareContexts = context;
    }

    private async ValueTask<object> CompleteFailedResolutionAsync(OwnedScope scope, List<object> failed, Exception error) {
        try {
            List<Exception> failures = await CleanupAsync(failed).ConfigureAwait(false);
            if (failures.Count != 0) {
                failures.Insert(0, error);
                throw new AggregateException("Activation cleanup failed.", failures);
            }

            ExceptionDispatchInfo.Capture(error).Throw();
            return null!;
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
        anchor.Cache.TryGetValue(service, out CacheSlot? slot);

        if (slot is null || !slot.Completion.Task.IsCompletedSuccessfully) {
            value = null!;
            return false;
        }

        Outcome outcome = slot.Completion.Task.GetAwaiter().GetResult();
        if (outcome.Error is not null) ExceptionDispatchInfo.Capture(outcome.Error).Throw();
        value = outcome.Value!;
        return true;
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

    private static bool Reaches(CacheSlot from, CacheSlot target, HashSet<CacheSlot> visited) =>
        from == target || visited.Add(from) && from.Dependencies.Any(next => Reaches(next, target, visited));

    internal static bool IsDisposable(object instance) => instance is IDisposable or IAsyncDisposable;
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

    internal sealed class ResolutionContext {
        private ServiceProvider _provider;
        private List<object>? _failed;
        private List<object>? _resources;
        internal ResolutionContext(ServiceProvider provider) => _provider = provider;
        internal ResolutionContext? Next { get; set; }
        internal List<object> Failed => _failed ??= [];
        internal List<ServiceRegistration> Path { get; } = [];
        internal int ResourceCount => _resources?.Count ?? 0;
        internal string PathText => string.Join(" -> ", Path.Select(r => r.Label));

        internal void AddResource(object resource) => (_resources ??= []).Add(resource);

        internal void CommitResources(OwnedScope owner, int start) {
            if (_resources is null || _resources.Count == start) return;
            lock (_provider.Gate) {
                for (int index = start; index < _resources.Count; index++) owner.Owned.Add(_resources[index]);
            }

            _resources.RemoveRange(start, _resources.Count - start);
        }

        internal void FailResources(int start) {
            if (_resources is null || _resources.Count == start) return;
            List<object> failed = Failed;
            for (int index = start; index < _resources.Count; index++) failed.Add(_resources[index]);
            _resources.RemoveRange(start, _resources.Count - start);
        }

        internal List<object> TakeFailed() {
            List<object> failed = _failed!;
            _failed = null;
            return failed;
        }

        internal void Reset(ServiceProvider? provider) {
            _provider = provider!;
            Path.Clear();
            _resources?.Clear();
            _failed?.Clear();
            Next = null;
        }
    }

    private sealed class FactoryResolver(
        ServiceProvider provider,
        ResolutionContext context,
        OwnedScope anchor,
        CacheSlot? slot
    ) : IServiceResolver {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _open = true;
        public T Get<T>() where T : notnull => (T)Get(typeof(T));
        public object Get(Type serviceType) {
            ArgumentNullException.ThrowIfNull(serviceType);
            if (!_open || Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("A factory resolver may only be used synchronously during its factory invocation.");

            return provider.Resolve(serviceType, context, anchor, slot);
        }
        internal void Close() => _open = false;
    }
}

internal sealed class CacheSlot(string label) {
    internal string Label { get; } = label;
    internal TaskCompletionSource<Outcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal HashSet<CacheSlot> Dependencies { get; } = [];
}

internal sealed record Outcome(object? Value, Exception? Error);
