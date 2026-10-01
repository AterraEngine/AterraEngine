// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>Available only during the synchronous factory invocation. Do not retain or share it.</summary>
public interface IServiceResolver {
    T Get<T>() where T : notnull;
    T GetKeyed<T, TKey>(TKey key) where T : notnull;
}
