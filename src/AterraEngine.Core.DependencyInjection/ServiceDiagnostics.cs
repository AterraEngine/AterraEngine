namespace AterraEngine.Core.DependencyInjection;

public interface IServiceDiagnosticSink {
    void Write(ServiceDiagnosticEvent diagnosticEvent);
}

public sealed class ServiceDiagnosticSink(Action<ServiceDiagnosticEvent> write) : IServiceDiagnosticSink {
    public void Write(ServiceDiagnosticEvent diagnosticEvent) => write(diagnosticEvent);
}

public sealed record ServiceDiagnosticsOptions(IServiceDiagnosticSink Sink, bool MeasureAllocations = false);

public enum ServiceDiagnosticEventKind {
    ActivationStarted,
    ActivationCompleted,
    ActivationFailed,
    CacheHit,
    CacheWait,
    ScopeCreated,
    ScopeDisposalStarted,
    CleanupCompleted
}

public enum ServiceDiagnosticActivationSource {
    Generated,
    Factory,
    Activator
}

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
