using System.Collections.Concurrent;

namespace AterraEngine.Core.DependencyInjection.Scopes;
/// <summary>Owns scoped services and disposable transients. Stop consumer jobs before shutdown.</summary>
public sealed class OwnedScope : IAsyncDisposable {
    private readonly List<OwnedScope> _children = [];
    private readonly ServiceProvider _provider;
    private int _active;
    private TaskCompletionSource? _disposed;
    private TaskCompletionSource? _idle;
    private bool _stopping;

    internal OwnedScope(ServiceProvider provider, Type scopeType, OwnedScope? parent, Dictionary<Type, object> inputs) {
        _provider = provider;
        ScopeType = scopeType;
        Parent = parent;
        Inputs = inputs;
        provider.TrackInputs(inputs.Values);
    }
    internal ConcurrentDictionary<Type, CacheSlot> Cache { get; } = [];
    internal List<object> Owned { get; } = [];
    internal Dictionary<Type, object> Inputs { get; }
    public Type ScopeType { get; }
    public OwnedScope? Parent { get; }

    public ValueTask DisposeAsync() {
        TaskCompletionSource completion;
        lock (_provider.Gate) {
            if (_disposed is not null) return new ValueTask(_disposed.Task);

            _disposed = completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            MarkStopping();
        }

        _ = DisposeCoreAsync(completion);
        return new ValueTask(completion.Task);
    }

    public OwnedScope CreateScope<TScope>(params ScopeInput[] inputs) {
        lock (_provider.Gate) {
            ThrowIfStopping();
            Type type = typeof(TScope);
            if (!_provider.Parents.TryGetValue(type, out Type[]? parents) || !parents.Contains(ScopeType))
                throw new DependencyInjectionException($"Scope {type.Name} cannot be created under {ScopeType.Name}.");

            for (OwnedScope? scope = this; scope is not null; scope = scope.Parent) {
                if (scope.ScopeType == type) throw new DependencyInjectionException($"Repeated scope type {type.Name} in ancestry.");
            }

            var child = new OwnedScope(_provider, type, this, _provider.ValidateInputs(type, inputs));
            _children.Add(child);
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

    private static async ValueTask<T> AwaitResolution<T>(ValueTask<object> resolution)
        => (T)await resolution.ConfigureAwait(false);

    internal void Enter() {
        lock (_provider.Gate) {
            ThrowIfStopping();
            _provider.RejectReentrantResolution();
            for (OwnedScope? scope = this; scope is not null; scope = scope.Parent) {
                scope._active++;
            }
        }
    }

    internal void Exit() {
        lock (_provider.Gate) {
            for (OwnedScope? scope = this; scope is not null; scope = scope.Parent) {
                if (--scope._active == 0) scope._idle?.TrySetResult();
            }
        }
    }

    private void ThrowIfStopping() {
        if (_stopping) throw new ObjectDisposedException(ScopeType.Name, "Scope shutdown has started.");
    }

    private void MarkStopping() {
        _stopping = true;
        foreach (OwnedScope child in _children) child.MarkStopping();
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion) {
        var errors = new List<Exception>();
        try {
            Task idle;
            lock (_provider.Gate) {
                idle = _active == 0 ? Task.CompletedTask : (_idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            await idle.ConfigureAwait(false);
            OwnedScope[] children;
            lock (_provider.Gate) {
                children = _children.ToArray();
            }

            for (int index = children.Length - 1; index >= 0; index--) {
                try { await children[index].DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) { errors.Add(exception); }
            }

            errors.AddRange(await _provider.CleanupAsync(Owned).ConfigureAwait(false));
        }
        catch (Exception exception) { errors.Add(exception); }
        finally {
            lock (_provider.Gate) {
                Cache.Clear();
                _provider.ReleaseInputs(Inputs.Values);
                Inputs.Clear();
                _children.Clear();
                Parent?._children.Remove(this);
                if (Parent is null) _provider.ReleaseProvider();
            }
        }

        if (errors.Count == 0) completion.SetResult();
        else completion.SetException(new AggregateException($"Cleanup failed in {ScopeType.Name}.", errors));
    }
}
