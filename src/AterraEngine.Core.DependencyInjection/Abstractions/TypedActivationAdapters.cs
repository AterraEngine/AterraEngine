// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal interface ITypedGeneratedActivator {
    object Invoke(ref GeneratedServiceResolver resolver);
}

internal sealed class TypedGeneratedActivator<T>(GeneratedServiceActivator<T> activator) : ITypedGeneratedActivator {
    public object Invoke(ref GeneratedServiceResolver resolver) => activator(ref resolver)!;
}

internal interface ITypedCollectionResolver {
    object Invoke(ref GeneratedServiceResolver resolver);
}

internal sealed class TypedCollectionResolver<T>(GeneratedServiceCollectionResolver<T> resolver) : ITypedCollectionResolver {
    public object Invoke(ref GeneratedServiceResolver value) => resolver(ref value);
}
