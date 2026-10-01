// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Concurrent;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Owns scoped services and disposable transients. Stop consumer jobs before shutdown.</summary>
public sealed class OwnedServiceScope : IServiceScope {
    private readonly ServiceProvider _provider;
    private int _active;
    private ConcurrentDictionary<Type, ServiceCacheEntry>? _cache;
    private List<OwnedServiceScope>? _children;
    private bool _completed;
    private TaskCompletionSource? _disposed;
    private TaskCompletionSource? _idle;
    private ConcurrentDictionary<IServiceRegistration, ServiceCacheEntry>? _registrationCache;
    private bool _stopping;


    // -----------------------------------------------------------------------------------------------------------------
    // Constructors
    // -----------------------------------------------------------------------------------------------------------------
    internal OwnedServiceScope(ServiceProvider provider, Type scopeType, OwnedServiceScope? parent, Dictionary<Type, object>? inputs) {
        _provider = provider;
        ScopeType = scopeType;
        Parent = parent;
        Inputs = inputs;
        if (inputs is not null) provider.TrackInputs(inputs.Values);
    }

    private List<object>? Owned { get; set; }

    internal Dictionary<Type, object>? Inputs { get; }

    public Type ScopeType { get; }
    public OwnedServiceScope? Parent { get; }

    internal ConcurrentDictionary<Type, ServiceCacheEntry> Cache => GetOrCreateCache();

    private bool IsEmpty => _active == 0
        && _children is null
        && _cache is null
        && _registrationCache is null
        && Owned is null
        && Inputs is null;

    public T Get<T>() where T : notnull => (T)_provider.ResolveSync(this, typeof(T));
    public T GetKeyed<T, TKey>(TKey key) where T : notnull => (T)_provider.ResolveSync(this, ServiceKey.Of<T, TKey>(key));
    public ServiceProvider ServiceProvider => _provider;

    public void Dispose() {
        TaskCompletionSource? completion;
        Task? existing;
        lock (_provider.Gate) {
            if (_completed) return;

            if (_disposed is not null) {
                existing = _disposed.Task;
                completion = null;
            }
            else if (IsEmpty) {
                CompleteEmpty();
                return;
            }
            else {
                existing = null;
                _disposed = completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                MarkStopping();
                _provider.EmitScope(ServiceDiagnosticEventKind.ScopeDisposalStarted, this);
            }
        }

        if (existing is not null) {
            existing.GetAwaiter().GetResult();
            return;
        }

        DisposeCore(completion!);
    }

    public ValueTask DisposeAsync() {
        TaskCompletionSource completion;
        lock (_provider.Gate) {
            if (_completed) return ValueTask.CompletedTask;
            if (_disposed is not null) return new ValueTask(_disposed.Task);

            if (IsEmpty) {
                CompleteEmpty();
                return ValueTask.CompletedTask;
            }

            _disposed = completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            MarkStopping();
            _provider.EmitScope(ServiceDiagnosticEventKind.ScopeDisposalStarted, this);
        }

        _ = DisposeCoreAsync(completion);
        return new ValueTask(completion.Task);
    }
    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    internal ConcurrentDictionary<Type, ServiceCacheEntry> GetOrCreateCache() => _cache ??= [];
    internal ConcurrentDictionary<IServiceRegistration, ServiceCacheEntry> GetOrCreateRegistrationCache() => _registrationCache ??= [];

    internal bool TryGetCacheEntry(IServiceRegistration registration, out ServiceCacheEntry? entry) {
        entry = null;
        return _registrationCache is not null && _registrationCache.TryGetValue(registration, out entry);
    }

    internal void AddOwned(object value) => (Owned ??= []).Add(value);

    public OwnedServiceScope CreateScope<TScope>(params ServiceScopeInput[] inputs) => CreateScope(typeof(TScope), inputs);

    public OwnedServiceScope CreateScope(Type scopeType, params ServiceScopeInput[] inputs) {
        ArgumentNullException.ThrowIfNull(scopeType);
        lock (_provider.Gate) {
            ThrowIfStopping();
            if (!_provider.Parents.TryGetValue(scopeType, out Type[]? parents) || !parents.Contains(ScopeType))
                throw new DependencyInjectionException($"Scope {scopeType.Name} cannot be created under {ScopeType.Name}.");

            for (OwnedServiceScope? scope = this; scope is not null; scope = scope.Parent) {
                if (scope.ScopeType == scopeType) throw new DependencyInjectionException($"Repeated scope type {scopeType.Name} in ancestry.");
            }

            var child = new OwnedServiceScope(_provider, scopeType, this, _provider.ValidateInputs(scopeType, inputs));
            (_children ??= []).Add(child);
            _provider.EmitScope(ServiceDiagnosticEventKind.ScopeCreated, child);
            return child;
        }
    }

    public ValueTask<T> ResolveAsync<T>() where T : notnull {
        ValueTask<object> resolution = _provider.ResolveAsync(this, typeof(T));
        return resolution.IsCompletedSuccessfully
            ? new ValueTask<T>((T)resolution.Result)
            : AwaitResolution<T>(resolution);
    }
    public ValueTask<T> ResolveKeyedAsync<T, TKey>(TKey key) where T : notnull
        => AwaitResolution<T>(_provider.ResolveAsync(this, ServiceKey.Of<T, TKey>(key)));
    public ValueTask<T> ResolveNamedAsync<T>(string name) where T : notnull
        => ResolveKeyedAsync<T, string>(name);
    public ValueTask<T[]> ResolveKeyedEnumerableAsync<T, TKey>(TKey key)
        => _provider.ResolveKeyedCollectionAsync<T, TKey>(this, key);

    private static async ValueTask<T> AwaitResolution<T>(ValueTask<object> resolution)
        => (T)await resolution.ConfigureAwait(false);

    internal void Enter() {
        lock (_provider.Gate) {
            ThrowIfStopping();
            _provider.RejectReentrantResolution();
            for (OwnedServiceScope? scope = this; scope is not null; scope = scope.Parent) {
                scope._active++;
            }
        }
    }

    internal void Exit() {
        lock (_provider.Gate) {
            for (OwnedServiceScope? scope = this; scope is not null; scope = scope.Parent) {
                if (--scope._active == 0) scope._idle?.TrySetResult();
            }
        }
    }

    private void ThrowIfStopping() {
        if (_stopping) throw new ObjectDisposedException(ScopeType.Name, "Scope shutdown has started.");
    }

    private void MarkStopping() {
        _stopping = true;
        if (_children is null) return;

        foreach (OwnedServiceScope child in _children) child.MarkStopping();
    }

    // Called under the provider gate. Empty scopes have no user cleanup or waiters.
    private void CompleteEmpty() {
        MarkStopping();
        _provider.EmitScope(ServiceDiagnosticEventKind.ScopeDisposalStarted, this);
        _provider.CleanupEmpty();
        if (Parent?._children is not null) Parent._children.Remove(this);
        if (Parent is null) _provider.ReleaseProvider();
        _completed = true;
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion) {
        List<Exception>? errors = null;
        try {
            Task idle;
            lock (_provider.Gate) {
                idle = _active == 0 ? Task.CompletedTask : (_idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            await idle.ConfigureAwait(false);
            OwnedServiceScope[] children;
            lock (_provider.Gate) {
                children = _children is null ? Array.Empty<OwnedServiceScope>() : _children.ToArray();
            }

            for (int index = children.Length - 1; index >= 0; index--) {
                try { await children[index].DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) { (errors ??= []).Add(exception); }
            }

            AddErrors(ref errors, Owned is null
                ? _provider.CleanupEmpty()
                : await _provider.CleanupAsync(Owned).ConfigureAwait(false));
        }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        finally {
            lock (_provider.Gate) {
                _cache?.Clear();
                _registrationCache?.Clear();
                if (Inputs is not null) {
                    _provider.ReleaseInputs(Inputs.Values);
                    Inputs.Clear();
                }

                Owned = null;
                _cache = null;
                _children?.Clear();
                if (Parent?._children is not null) Parent._children.Remove(this);
                if (Parent is null) _provider.ReleaseProvider();
            }
        }

        if (errors is null) completion.SetResult();
        else completion.SetException(new AggregateException($"Cleanup failed in {ScopeType.Name}.", errors));
    }

    private void DisposeCore(TaskCompletionSource completion) {
        List<Exception>? errors = null;
        try {
            Task idle;
            lock (_provider.Gate) {
                idle = _active == 0 ? Task.CompletedTask : (_idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            idle.GetAwaiter().GetResult();
            OwnedServiceScope[] children;
            lock (_provider.Gate) {
                children = _children is null ? Array.Empty<OwnedServiceScope>() : _children.ToArray();
            }

            for (int index = children.Length - 1; index >= 0; index--) {
                try { children[index].Dispose(); }
                catch (Exception exception) { (errors ??= []).Add(exception); }
            }

            AddErrors(ref errors, Owned is null ? _provider.CleanupEmpty() : _provider.Cleanup(Owned));
        }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        finally {
            lock (_provider.Gate) {
                _cache?.Clear();
                _registrationCache?.Clear();
                if (Inputs is not null) {
                    _provider.ReleaseInputs(Inputs.Values);
                    Inputs.Clear();
                }

                Owned = null;
                _cache = null;
                _children?.Clear();
                if (Parent?._children is not null) Parent._children.Remove(this);
                if (Parent is null) _provider.ReleaseProvider();
            }
        }

        if (errors is null) completion.SetResult();
        else completion.SetException(new AggregateException($"Cleanup failed in {ScopeType.Name}.", errors));
        completion.Task.GetAwaiter().GetResult();
    }

    private static void AddErrors(ref List<Exception>? target, List<Exception> errors) {
        if (errors.Count == 0) return;

        (target ??= []).AddRange(errors);
    }
}
