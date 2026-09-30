// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public interface IGeneratedServiceProvider {
    object ResolveGenerated(Type service, IGeneratedServiceResolutionContext context, IGeneratedServiceScope anchor, IGeneratedServiceCacheEntry? cacheEntry);
    object ResolveGeneratedKeyed(Type service, Type keyType, object? key, IGeneratedServiceResolutionContext context, IGeneratedServiceScope anchor, IGeneratedServiceCacheEntry? cacheEntry);
    object ResolveGeneratedInner<T>(IGeneratedServiceRegistration? inner, IGeneratedServiceResolutionContext context, IGeneratedServiceScope anchor, IGeneratedServiceCacheEntry? cacheEntry) where T : notnull;
    T[] ResolveGeneratedCollection<T>(IGeneratedServiceResolutionContext context, IGeneratedServiceScope anchor, IGeneratedServiceCacheEntry? cacheEntry);
    T[] ResolveGeneratedKeyedCollection<T, TKey>(TKey key, IGeneratedServiceResolutionContext context, IGeneratedServiceScope anchor, IGeneratedServiceCacheEntry? cacheEntry);
}
