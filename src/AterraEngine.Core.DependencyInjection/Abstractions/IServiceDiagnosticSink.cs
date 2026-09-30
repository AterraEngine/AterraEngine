namespace AterraEngine.Core.DependencyInjection;
public interface IServiceDiagnosticSink {
    void Write(ServiceDiagnosticEvent diagnosticEvent);
}
