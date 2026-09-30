// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed record ServiceDiagnosticEvent {
    public ServiceDiagnosticEventKind Kind { get; init; }
    public long Sequence { get; init; }
    public Type? ServiceType { get; init; }
    public Type? KeyType { get; init; }
    public object? Key { get; init; }
    public Type? ScopeType { get; init; }
    public Type? LifetimeScopeType { get; init; }
    public ServiceDiagnosticActivationSource? ActivationSource { get; init; }
    public IReadOnlyList<string> ResolutionPath { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public long? AllocatedBytes { get; init; }
    public Exception? Error { get; init; }
}
