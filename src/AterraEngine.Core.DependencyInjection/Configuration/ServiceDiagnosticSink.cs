// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class ServiceDiagnosticSink(Action<ServiceDiagnosticEvent> write) : IServiceDiagnosticSink {
    public void Write(ServiceDiagnosticEvent diagnosticEvent) => write(diagnosticEvent);
}
