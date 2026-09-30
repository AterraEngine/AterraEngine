// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public interface IGeneratedServiceResolutionContext {
    IReadOnlyList<IGeneratedServiceRegistration> Path { get; }
    IReadOnlyCollection<object> Failed { get; }
    int ResourceCount { get; }
    string PathText { get; }
}
