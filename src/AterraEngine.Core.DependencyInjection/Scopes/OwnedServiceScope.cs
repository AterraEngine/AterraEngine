// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Concurrent;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Owns scoped services and disposable transients. Stop consumer jobs before shutdown.</summary>
public sealed class OwnedServiceScope : IServiceScope, IServiceProvider {
    private List<OwnedServiceScope>? _children;
    private ConcurrentDictionary<Type, ServiceCacheEntry>? _cache;
    private ConcurrentDictionary<ServiceRegistration, ServiceCacheEntry>? _registrationCache;
    private List<object>? _owned;
    private readonly ServiceProvider _provider;
    private int _active;
    private TaskCompletionSource? _disposed;
    private TaskCompletionSource? _idle;
    private bool _stopping;

    internal OwnedServiceScope(ServiceProvider provider, Type scopeType, OwnedServiceScope? parent, Dictionary<Type, object> inputs) {
        _provider = provider;
        ScopeType = scopeType;
        Parent = parent;
        Inputs = inputs;
        provider.TrackInputs(inputs.Values);
    }
    internal ConcurrentDictionary<Type, ServiceCacheEntry> GetOrCreateCache() => _cache ??= [];
    internal ConcurrentDictionary<ServiceRegistration, ServiceCacheEntry> GetOrCreateRegistrationCache() => _registrationCache ??= [];
    internal ConcurrentDictionary<Type, ServiceCacheEntry> Cache => GetOrCreateCache();
    internal bool TryGetCacheEntry(Type service, out ServiceCacheEntry? entry) {
        entry = null;
        return _cache is not null && _cache.TryGetValue(service, out entry);
    }
    internal bool TryGetCacheEntry(ServiceRegistration registration, out ServiceCacheEntry? entry) {
        entry = null;
        return _registrationCache is not null && _registrationCache.TryGetValue(registration, out entry);
    }
    internal void AddOwned(object value) => (_owned ??= []).Add(value);
    internal List<object>? Owned => _owned;
    internal Dictionary<Type, object> Inputs { get; }
    public Type ScopeType { get; }
    public OwnedServiceScope? Parent { get; }
    public IServiceProvider ServiceProvider => this;

    /// <summary>Resolves through this scope, rather than its parent host.</summary>
    public object? GetService(Type serviceType) => _provider.GetService(this, serviceType);

    public void Dispose() {
        TaskCompletionSource? completion;
        Task? existing;
        lock (_provider.Gate) {
            if (_disposed is not null) {
                existing = _disposed.Task;
                completion = null;
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
            if (_disposed is not null) return new ValueTask(_disposed.Task);

            _disposed = completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            MarkStopping();
            _provider.EmitScope(ServiceDiagnosticEventKind.ScopeDisposalStarted, this);
        }

        _ = DisposeCoreAsync(completion);
        return new ValueTask(completion.Task);
    }

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
        ValueTask<object> resolution = ResolveAsync(typeof(T));
        return resolution.IsCompletedSuccessfully
            ? new ValueTask<T>((T)resolution.Result)
            : AwaitResolution<T>(resolution);
    }
    public ValueTask<object> ResolveAsync(Type serviceType) => _provider.ResolveAsync(this, serviceType);
    public ValueTask<T> ResolveKeyedAsync<T, TKey>(TKey key) where T : notnull => AwaitResolution<T>(_provider.ResolveAsync(this, ServiceKey.Of<T, TKey>(key)));
    public ValueTask<T> ResolveNamedAsync<T>(string name) where T : notnull => ResolveKeyedAsync<T, string>(name);
    public ValueTask<T[]> ResolveKeyedEnumerableAsync<T, TKey>(TKey key) => _provider.ResolveKeyedCollectionAsync<T, TKey>(this, key);
    public ValueTask<object> ResolveKeyedAsync(Type serviceType, Type keyType, object? key)
        => _provider.ResolveAsync(this, new ServiceKey(serviceType, keyType, key));

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

            AddErrors(ref errors, _owned is null
                ? _provider.CleanupEmpty()
                : await _provider.CleanupAsync(_owned).ConfigureAwait(false));
        }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        finally {
            lock (_provider.Gate) {
                _cache?.Clear();
                _registrationCache?.Clear();
                _provider.ReleaseInputs(Inputs.Values);
                Inputs.Clear();
                _owned = null;
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

            AddErrors(ref errors, _owned is null ? _provider.CleanupEmpty() : _provider.Cleanup(_owned));
        }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        finally {
            lock (_provider.Gate) {
                _cache?.Clear();
                _registrationCache?.Clear();
                _provider.ReleaseInputs(Inputs.Values);
                Inputs.Clear();
                _owned = null;
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
