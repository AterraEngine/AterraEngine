// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
///     Project-owned scope factory with the standard parameterless entry point and
///     Aterra's explicit runtime scope creation entry point.
/// </summary>
public interface IServiceScopeFactory {
    IServiceScope CreateScope();
    IServiceScope CreateScope(Type scopeType, params ServiceScopeInput[] inputs);
    IServiceScope CreateAsyncScope();
    IServiceScope CreateAsyncScope(Type scopeType, params ServiceScopeInput[] inputs);
}
