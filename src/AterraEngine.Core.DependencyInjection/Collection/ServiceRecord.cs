// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection.Collection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed record ServiceRecord(ServiceLifetime Lifetime, Type Service, Type Implementation, string? Module = null) {
    public ServiceRecord(ServiceScope scope, Type service, Type implementation)
        : this(scope switch {
            ServiceScope.Transient => ServiceLifetime.Transient,
            ServiceScope.Singleton => ServiceLifetime.Singleton,
            ServiceScope.Host => ServiceLifetime.Host,
            ServiceScope.World => ServiceLifetime.Of<World>(),
            ServiceScope.Scene => ServiceLifetime.Of<Scene>(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        }, service, implementation) {
    }
}
