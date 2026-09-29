// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class CacheSlot(string label) {
    internal string Label { get; } = label;
    internal TaskCompletionSource<ServiceOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal HashSet<CacheSlot> Dependencies { get; } = [];
}
