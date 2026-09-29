namespace AterraEngine.Core.DependencyInjection;
/// <summary>Available only during the synchronous factory invocation. Do not retain or share it.</summary>
public interface IServiceResolver {
    T Get<T>() where T : notnull;
    object Get(Type serviceType);
}
