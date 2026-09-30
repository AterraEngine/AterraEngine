namespace AterraEngine.Core.DependencyInjection;
public sealed record ServiceDiagnosticsOptions(IServiceDiagnosticSink Sink, bool MeasureAllocations = false);
