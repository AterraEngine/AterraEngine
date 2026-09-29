using System.Runtime.ExceptionServices;
using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection;
/// <summary>A single engine host. Shutdown and failed-activation cleanup are asynchronous.</summary>
public sealed class ServiceProvider : IAsyncDisposable {
    private readonly Dictionary<int, int> _activationThreads = [];
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
        Dictionary<Type, object> hostInputs = ValidateInputs(typeof(Host), inputs);
        foreach (ServiceRegistration registration in registrations.Values) {
            if (registration.Instance is {} instance && hostInputs.Values.Any(input => ReferenceEquals(input, instance)))
                throw registration.Error("An external service cannot also be registered as a scope input.");
        }

        Host = new OwnedScope(this, typeof(Host), null, hostInputs);
        foreach (ServiceRegistration registration in registrations.Values) {
            if (registration.Instance is not {} instance) continue;

            _claimed.Add(instance, ++_constructionOrder);
            if (registration.Ownership == InstanceOwnership.Container && IsDisposable(instance)) Host.Owned.Add(instance);
        }
    }
    internal object Gate { get; } = new();
    internal Dictionary<Type, Type[]> Parents { get; }
    internal Dictionary<Type, Type> InputOwners { get; }
    public OwnedScope Host { get; }
    public ValueTask DisposeAsync() => Host.DisposeAsync();

    public ValueTask<T> ResolveAsync<T>() where T : notnull => Host.ResolveAsync<T>();
    public OwnedScope CreateScope<TScope>(params ScopeInput[] inputs) => Host.CreateScope<TScope>(inputs);

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

    internal async ValueTask<object> ResolveAsync(OwnedScope scope, Type service) {
        ArgumentNullException.ThrowIfNull(service);
        scope.Enter();
        var operation = new Resolution();
        var root = new Frame(scope, null, [], operation);
        object? result = null;
        Exception? error = null;
        try {
            try {
                result = Resolve(service, root);
                Commit(root);
            }
            catch (Exception exception) {
                error = exception;
                operation.Failed.AddRange(root.Resources);
                root.Resources.Clear();
            }

            List<Exception> failures = await CleanupAsync(operation.Failed).ConfigureAwait(false);
            if (failures.Count != 0) {
                if (error is not null) failures.Insert(0, error);
                throw new AggregateException("Activation cleanup failed.", failures);
            }

            if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
            return result!;
        }
        finally { scope.Exit(); }
    }

    private object Resolve(Type service, Frame caller) {
        if (InputOwners.TryGetValue(service, out Type? inputOwner)) {
            OwnedScope owner = FindOwner(caller.Anchor, inputOwner, $"input {service}");
            return owner.Inputs[service];
        }

        if (!_registrations.TryGetValue(service, out ServiceRegistration? registration))
            throw new DependencyInjectionException($"Unregistered service {service}; path: {caller.PathText}.");
        if (caller.Path.Contains(registration)) throw registration.Error($"Dependency cycle: {caller.PathText} -> {registration.Label}.");

        Type? scopeType = registration.Record.Lifetime.ScopeType;
        OwnedScope anchor = scopeType is null ? caller.Anchor : FindOwner(caller.Anchor, scopeType, registration.Label);
        if (registration.Instance is {} instance) return instance;
        if (scopeType is null) return Activate(registration, anchor, caller.Slot, caller, false);

        CacheSlot slot;
        bool construct;
        lock (Gate) {
            construct = !anchor.Cache.TryGetValue(service, out slot!);
            if (construct) anchor.Cache.Add(service, slot = new CacheSlot(registration.Label));
            if (caller.Slot is {} parent && !slot.Completion.Task.IsCompleted) {
                if (Reaches(slot, parent, [])) throw registration.Error($"Concurrent dependency cycle between {parent.Label} and {slot.Label}.");

                parent.Dependencies.Add(slot);
            }
        }

        try {
            if (construct) {
                Outcome outcome;
                try { outcome = new Outcome(Activate(registration, anchor, slot, caller, true), null); }
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
                caller.Slot?.Dependencies.Remove(slot);
            }
        }
    }

    private object Activate(ServiceRegistration registration, OwnedScope anchor, CacheSlot? slot, Frame caller, bool cached) {
        var frame = new Frame(anchor, slot, [.. caller.Path, registration], caller.Operation);
        var resolver = new FactoryResolver(this, frame);
        int thread = Environment.CurrentManagedThreadId;
        lock (Gate) {
            _activationThreads[thread] = _activationThreads.GetValueOrDefault(thread) + 1;
        }

        try {
            object value = (registration.Factory ?? registration.Activator!.Create)(resolver);
            if (value is null || !registration.Record.Service.IsInstanceOfType(value)) throw registration.Error("Factory returned null or an incompatible object.");

            if (IsDisposable(value)) {
                lock (Gate) {
                    if (_external.ContainsKey(value) || !_claimed.TryAdd(value, ++_constructionOrder))
                        throw registration.Error("Factory returned an object already owned or registered externally. Return a new instance.");
                }

                frame.Resources.Add(value);
            }

            if (cached) Commit(frame);
            else caller.Resources.AddRange(frame.Resources);
            return value;
        }
        catch (Exception exception) {
            caller.Operation.Failed.AddRange(frame.Resources);
            frame.Resources.Clear();
            if (exception is DependencyInjectionException) throw;

            throw registration.Error("Activation failed.", exception);
        }
        finally {
            resolver.Close();
            lock (Gate) {
                if (--_activationThreads[thread] == 0) _activationThreads.Remove(thread);
            }
        }
    }

    private void Commit(Frame frame) {
        lock (Gate) {
            frame.Anchor.Owned.AddRange(frame.Resources);
        }

        frame.Resources.Clear();
    }

    private static OwnedScope FindOwner(OwnedScope from, Type type, string service) {
        for (OwnedScope? scope = from; scope is not null; scope = scope.Parent) {
            if (scope.ScopeType == type) return scope;
        }

        throw new DependencyInjectionException($"Missing ownership scope {type.Name} for {service}, resolving from {from.ScopeType.Name}. Descendants and siblings are not visible.");
    }

    private static bool Reaches(CacheSlot from, CacheSlot target, HashSet<CacheSlot> visited) =>
        from == target || visited.Add(from) && from.Dependencies.Any(next => Reaches(next, target, visited));

    internal static bool IsDisposable(object instance) => instance is IDisposable or IAsyncDisposable;

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

    internal void ReleaseHost() {
        _claimed.Clear();
        _external.Clear();
        _registrations.Clear();
    }

    internal void RejectReentrantResolution() {
        if (_activationThreads.ContainsKey(Environment.CurrentManagedThreadId))
            throw new DependencyInjectionException("Reentrant public resolution during activation is prohibited; factories must use their supplied IServiceResolver to preserve cycle and ownership checks.");
    }

    internal void TrackInputs(IEnumerable<object> inputs) {
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

    private sealed class Resolution {
        internal List<object> Failed { get; } = [];
    }

    private sealed class Frame(OwnedScope anchor, CacheSlot? slot, List<ServiceRegistration> path, Resolution operation) {
        internal OwnedScope Anchor { get; } = anchor;
        internal CacheSlot? Slot { get; } = slot;
        internal List<ServiceRegistration> Path { get; } = path;
        internal Resolution Operation { get; } = operation;
        internal List<object> Resources { get; } = [];
        internal string PathText => string.Join(" -> ", Path.Select(r => r.Label));
    }

    private sealed class FactoryResolver(ServiceProvider provider, Frame frame) : IServiceResolver {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _open = true;
        public T Get<T>() where T : notnull => (T)Get(typeof(T));
        public object Get(Type serviceType) {
            ArgumentNullException.ThrowIfNull(serviceType);
            if (!_open || Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("A factory resolver may only be used synchronously during its factory invocation.");

            return provider.Resolve(serviceType, frame);
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
