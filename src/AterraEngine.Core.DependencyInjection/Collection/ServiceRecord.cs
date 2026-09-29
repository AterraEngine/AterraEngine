// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection.Collection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed record ServiceRecord(Lifetime Lifetime, Type Service, Type Implementation, string? Module = null) {
    public ServiceRecord(ServiceScope scope, Type service, Type implementation)
        : this(scope switch {
            ServiceScope.Transient => Lifetime.Transient,
            ServiceScope.Singleton => Lifetime.Singleton,
            ServiceScope.Host => Lifetime.Host,
            ServiceScope.World => Lifetime.Of<World>(),
            ServiceScope.Scene => Lifetime.Of<Scene>(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        }, service, implementation) {
    }
}
