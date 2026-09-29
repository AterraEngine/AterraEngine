namespace AterraEngine.Core.DependencyInjection;

/// <summary>
/// Project-owned equivalent of Microsoft's <c>IServiceScope</c>. It is kept
/// package-free so the core container does not acquire a Microsoft DI runtime
/// contract dependency.
/// </summary>
public interface IServiceScope : IDisposable, IAsyncDisposable {
    IServiceProvider ServiceProvider { get; }
}

/// <summary>
/// Project-owned scope factory with the standard parameterless entry point and
/// Aterra's explicit runtime scope creation entry point.
/// </summary>
public interface IServiceScopeFactory {
    IServiceScope CreateScope();
    IServiceScope CreateScope(Type scopeType, params ServiceScopeInput[] inputs);
    IServiceScope CreateAsyncScope();
    IServiceScope CreateAsyncScope(Type scopeType, params ServiceScopeInput[] inputs);
}
