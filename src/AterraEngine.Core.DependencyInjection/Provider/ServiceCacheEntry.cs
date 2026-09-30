// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Tracks the activation result and in-progress dependencies of one service cache entry.</summary>
public sealed class ServiceCacheEntry(string label) : IGeneratedServiceCacheEntry {
    private readonly Lock Lock = new();
    private readonly HashSet<IGeneratedServiceCacheEntry> _generatedDependencies = [];
    private int _completed;
    private TaskCompletionSource<ServiceOutcome>? _completion;
    private ServiceOutcome? _outcome;

    private object? _publishedValue;
    internal string Label { get; } = label;

    internal HashSet<ServiceCacheEntry>? Dependencies { get; private set; }
    internal bool IsCompleted => Volatile.Read(ref _completed) != 0;

    string IGeneratedServiceCacheEntry.Label => Label;
    bool IGeneratedServiceCacheEntry.IsCompleted => IsCompleted;
    IReadOnlyCollection<IGeneratedServiceCacheEntry> IGeneratedServiceCacheEntry.Dependencies
        => _generatedDependencies;

    internal void AddDependency(ServiceCacheEntry entry) {
        (Dependencies ??= []).Add(entry);
        _generatedDependencies.Add(entry);
    }

    internal void SetOutcome(ServiceOutcome outcome) {
        lock (Lock) {
            if (_completed != 0) return;

            _outcome = outcome;
            Volatile.Write(ref _completed, 1);
            _completion?.TrySetResult(outcome);
        }
    }

    internal bool TryGetOutcome(out ServiceOutcome outcome) {
        outcome = _outcome!.Value;
        return IsCompleted;
    }

    internal Task<ServiceOutcome> GetCompletionTask() {
        lock (Lock) {
            return _completed != 0
                ? Task.FromResult(_outcome!.Value)
                : (_completion ??= new TaskCompletionSource<ServiceOutcome>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    internal void Publish(object value) => Volatile.Write(ref _publishedValue, value);

    internal bool TryGetPublishedValue(out object value) {
        value = Volatile.Read(ref _publishedValue)!;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return value is not null;
    }
}
