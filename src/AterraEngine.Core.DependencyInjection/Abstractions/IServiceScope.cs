// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>
///     Project-owned equivalent of Microsoft's <c>IServiceScope</c>. It is kept
///     package-free so the core container does not acquire a Microsoft DI runtime
///     contract dependency.
/// </summary>
public interface IServiceScope : IDisposable, IAsyncDisposable {
    IServiceProvider ServiceProvider { get; }
}
