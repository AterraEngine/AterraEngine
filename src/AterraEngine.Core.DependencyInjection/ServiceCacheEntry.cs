// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Tracks the activation result and in-progress dependencies of one service cache entry.</summary>
public sealed class ServiceCacheEntry(string label) {
    internal string Label { get; } = label;
    internal TaskCompletionSource<ServiceOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal HashSet<ServiceCacheEntry> Dependencies { get; } = [];
}
