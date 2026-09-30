// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection.Tests.Fixtures;
using JetBrains.Annotations;

namespace AterraEngine.Core.DependencyInjection.Tests.Collection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public class ServiceCollectionValidatorTests {
    [Test]
    public void AcceptsValidBranchingScopesDependenciesInputsFactoriesAndInstances() {
        // Arrange
        Dictionary<Type, Type[]> parents = DefaultParents();
        parents.Add(typeof(LeftScope), [typeof(AterraWorld)]);
        parents.Add(typeof(RightScope), [typeof(AterraWorld)]);
        parents.Add(typeof(JoinedScope), [typeof(LeftScope), typeof(RightScope)]);

        var inputs = new Dictionary<Type, Type> { [typeof(WorldInput)] = typeof(AterraWorld) };
        var registrations = new Dictionary<Type, ServiceRegistration>();
        var activators = new Dictionary<Type, ServiceActivationPlan>();
        Add(registrations, activators, typeof(WorldDependency), ServiceLifetime.Of<AterraWorld>());
        Add(registrations, activators, typeof(JoinedConsumer), ServiceLifetime.Of<JoinedScope>(), typeof(WorldDependency), typeof(WorldInput));

        ServiceRegistration factory = ServiceRegistration.AsFactory(
            new ServiceRecord(ServiceLifetime.Host, typeof(FactoryService), typeof(FactoryService)),
            factory: _ => new FactoryService()
        );
        registrations.Add(typeof(FactoryService), factory);

        var instance = new InstanceService();
        registrations.Add(typeof(InstanceService), ServiceRegistration.AsInstance(
            new ServiceRecord(ServiceLifetime.Host, typeof(InstanceService), typeof(InstanceService)), instance, ServiceInstanceOwnership.Caller
        ));
        registrations.Add(typeof(ValueInstance), ServiceRegistration.AsInstance(
            new ServiceRecord(ServiceLifetime.Host, typeof(ValueInstance), typeof(ValueInstance)), new ValueInstance(1), ServiceInstanceOwnership.Caller
        ));
        registrations.Add(typeof(IValueInstance), ServiceRegistration.AsInstance(
            new ServiceRecord(ServiceLifetime.Host, typeof(IValueInstance), typeof(ValueInstance)), new ValueInstance(1), ServiceInstanceOwnership.Caller
        ));

        // Act
        ServiceCollectionValidator.Validate(activators, inputs, parents, registrations);

        // Assert
        Check.True(registrations[typeof(JoinedConsumer)].Activator == activators[typeof(JoinedConsumer)], "Activator was not attached.");
        Check.True(factory.Activator is null, "A factory should not receive a generated activator.");
    }

    [Test]
    public void RejectsUndeclaredInvalidAndCyclicScopes() {
        // Arrange
        Dictionary<Type, Type[]> undeclared = DefaultParents();
        undeclared.Add(typeof(LeftScope), [typeof(UnknownScope)]);

        Dictionary<Type, Type[]> noHostPath = DefaultParents();
        noHostPath.Add(typeof(LeftScope), []);

        Dictionary<Type, Type[]> openGeneric = DefaultParents();
        openGeneric.Add(typeof(GenericScope<>), [typeof(AterraHost)]);

        Dictionary<Type, Type[]> voidScope = DefaultParents();
        voidScope.Add(typeof(void), [typeof(AterraHost)]);

        Dictionary<Type, Type[]> cycle = DefaultParents();
        cycle.Add(typeof(LeftScope), [typeof(RightScope)]);
        cycle.Add(typeof(RightScope), [typeof(LeftScope)]);

        // Act
        Action validateUndeclared = Validation(parents: undeclared);
        Action validateNoHostPath = Validation(parents: noHostPath);
        Action validateOpenGeneric = Validation(parents: openGeneric);
        Action validateVoidScope = Validation(parents: voidScope);
        Action validateCycle = Validation(parents: cycle);

        // Assert
        Check.Fails<DependencyInjectionException>(validateUndeclared, "Undeclared scope");
        Check.Fails<DependencyInjectionException>(validateNoHostPath, "path to AterraSingleton");
        Check.Fails<DependencyInjectionException>(validateOpenGeneric, "Invalid scope");
        Check.Fails<DependencyInjectionException>(validateVoidScope, "Invalid scope");
        Check.Fails<DependencyInjectionException>(validateCycle, "cycle");
    }

    [Test]
    public void RejectsVeryDeepScopeCycleWithoutUsingTheCallStack() {
        // Arrange
        const int depth = 10_000;
        Type[] scopes = UniqueTypes(depth);
        Dictionary<Type, Type[]> parents = DefaultParents();
        for (int index = 0; index < depth - 1; index++) {
            parents.Add(scopes[index], [scopes[index + 1]]);
        }

        parents.Add(scopes[^1], [scopes[0]]);

        // Act
        Action validate = Validation(parents: parents);

        // Assert
        Check.Fails<DependencyInjectionException>(validate, "cycle");
    }

    [Test]
    public void RejectsInvalidInputDeclarations() {
        // Arrange
        var undeclaredScope = new Dictionary<Type, Type> {
            [typeof(WorldInput)] = typeof(UnknownScope)
        };
        var openGeneric = new Dictionary<Type, Type> {
            [typeof(GenericInput<>)] = typeof(AterraWorld)
        };
        var voidInput = new Dictionary<Type, Type> {
            [typeof(void)] = typeof(AterraWorld)
        };

        // Act
        Action validateUndeclaredScope = Validation(inputs: undeclaredScope);
        Action validateOpenGeneric = Validation(inputs: openGeneric);
        Action validateVoidInput = Validation(inputs: voidInput);

        // Assert
        Check.Fails<DependencyInjectionException>(validateUndeclaredScope, "Invalid input declaration");
        Check.Fails<DependencyInjectionException>(validateOpenGeneric, "Invalid input declaration");
        Check.Fails<DependencyInjectionException>(validateVoidInput, "Invalid input declaration");
    }

    [Test]
    public void RejectsInvalidServiceTypesAndUndeclaredLifetimeScopes() {
        // Arrange
        Type[] invalidServices = [typeof(void), typeof(GenericService<>), typeof(int).MakeByRefType(), typeof(int).MakePointerType()];
        Action[] validateInvalidServices = invalidServices.Select(service => {
            var registration = new ServiceRegistration(new ServiceRecord(ServiceLifetime.Host, service, typeof(ValidService)));
            return Validation(registrations: new Dictionary<Type, ServiceRegistration> { [service] = registration });
        }).ToArray();

        var unknownLifetime = new ServiceRegistration(
            new ServiceRecord(ServiceLifetime.Of<UnknownScope>(), typeof(ValidService), typeof(ValidService))
        );
        var registrations = new Dictionary<Type, ServiceRegistration> {
            [typeof(ValidService)] = unknownLifetime
        };

        // Act
        Action validateUnknownLifetime = Validation(registrations: registrations);

        // Assert
        foreach (Action validate in validateInvalidServices) {
            Check.Fails<DependencyInjectionException>(validate, "closed, resolvable type");
        }

        Check.Fails<DependencyInjectionException>(validateUnknownLifetime, "Undeclared lifetime scope");
    }

    [Test]
    public void RejectsInvalidImplementationsAndMissingActivators() {
        // Arrange
        Type[] invalidImplementations = [typeof(UnrelatedService), typeof(IContract), typeof(AbstractImplementation), typeof(GenericImplementation<>)];

        // Act
        Action[] validateInvalidImplementations = invalidImplementations
            .Select(implementation => RegistrationValidation(typeof(IContract), implementation))
            .ToArray();
        Action validateMissingActivator = RegistrationValidation(typeof(ValidService), typeof(ValidService));

        // Assert
        foreach (Action validate in validateInvalidImplementations) {
            Check.Fails<DependencyInjectionException>(validate, "Invalid implementation");
        }

        Check.Fails<DependencyInjectionException>(validateMissingActivator, "No generated activator");
    }

    [Test]
    public void RejectsTheSameExternalObjectRegisteredMoreThanOnce() {
        // Arrange
        var external = new InstanceService();
        var registrations = new Dictionary<Type, ServiceRegistration> {
            [typeof(InstanceService)] = ServiceRegistration.AsInstance(
                new ServiceRecord(ServiceLifetime.Host, typeof(InstanceService), typeof(InstanceService)), external, ServiceInstanceOwnership.Caller
            ),
            [typeof(IContract)] = ServiceRegistration.AsInstance(
                new ServiceRecord(ServiceLifetime.Host, typeof(IContract), typeof(InstanceService)), external, ServiceInstanceOwnership.Caller
            )
        };

        // Act
        Action validate = Validation(registrations: registrations);

        // Assert
        Check.Fails<DependencyInjectionException>(validate, "same external object");
    }

    [Test]
    public void RejectsMissingDependenciesAndReportsTheFullPath() {
        // Arrange
        var registrations = new Dictionary<Type, ServiceRegistration>();
        var activators = new Dictionary<Type, ServiceActivationPlan>();
        Add(registrations, activators, typeof(RootService), ServiceLifetime.Host, typeof(MiddleService));
        Add(registrations, activators, typeof(MiddleService), ServiceLifetime.Transient, typeof(UnregisteredService));

        // Act
        Action validate = Validation(activators, registrations: registrations);

        // Assert
        var exception = Check.Fails<DependencyInjectionException>(validate, "Missing dependency");
        Check.True(exception.Message.Contains(nameof(RootService)), "The root service is absent from the dependency path.");
        Check.True(exception.Message.Contains(nameof(MiddleService)), "The intermediate service is absent from the dependency path.");
        Check.True(exception.Message.Contains(typeof(UnregisteredService).ToString()), "The missing service type is absent from the error.");
    }

    [Test]
    public void RejectsDependencyCyclesAndReportsTheCycle() {
        // Arrange
        var registrations = new Dictionary<Type, ServiceRegistration>();
        var activators = new Dictionary<Type, ServiceActivationPlan>();
        Add(registrations, activators, typeof(RootService), ServiceLifetime.Host, typeof(MiddleService));
        Add(registrations, activators, typeof(MiddleService), ServiceLifetime.Transient, typeof(LeafService));
        Add(registrations, activators, typeof(LeafService), ServiceLifetime.Transient, typeof(MiddleService));

        // Act
        Action validate = Validation(activators, registrations: registrations);

        // Assert
        var exception = Check.Fails<DependencyInjectionException>(validate, "Dependency cycle");
        Check.True(exception.Message.Contains("RootService -> MiddleService -> LeafService -> MiddleService"), "The complete cycle was not reported.");
    }

    [Test]
    public void ValidatesSharedTransientDependenciesForEveryLifetimeAnchor() {
        // Arrange
        var registrations = new Dictionary<Type, ServiceRegistration>();
        var activators = new Dictionary<Type, ServiceActivationPlan>();
        Add(registrations, activators, typeof(WorldRoot), ServiceLifetime.Of<AterraWorld>(), typeof(SharedTransient));
        Add(registrations, activators, typeof(SharedTransient), ServiceLifetime.Transient, typeof(WorldDependency));
        Add(registrations, activators, typeof(WorldDependency), ServiceLifetime.Of<AterraWorld>());
        Add(registrations, activators, typeof(HostRoot), ServiceLifetime.Host, typeof(SharedTransient));

        // Act
        Action validate = Validation(activators, registrations: registrations);

        // Assert
        Check.Fails<DependencyInjectionException>(validate, "Lifetime violation");
    }

    [Test]
    public void RejectsDependenciesOnSiblingScopesAndDescendantInputs() {
        // Arrange
        Dictionary<Type, Type[]> parents = DefaultParents();
        parents.Add(typeof(LeftScope), [typeof(AterraWorld)]);
        parents.Add(typeof(RightScope), [typeof(AterraWorld)]);
        parents.Add(typeof(JoinedScope), [typeof(LeftScope), typeof(RightScope)]);

        var siblingRegistrations = new Dictionary<Type, ServiceRegistration>();
        var siblingActivators = new Dictionary<Type, ServiceActivationPlan>();
        Add(siblingRegistrations, siblingActivators, typeof(JoinedConsumer), ServiceLifetime.Of<JoinedScope>(), typeof(LeftScopedService));
        Add(siblingRegistrations, siblingActivators, typeof(LeftScopedService), ServiceLifetime.Of<LeftScope>());

        var inputRegistrations = new Dictionary<Type, ServiceRegistration>();
        var inputActivators = new Dictionary<Type, ServiceActivationPlan>();
        var inputs = new Dictionary<Type, Type> { [typeof(SceneInput)] = typeof(AterraScene) };
        Add(inputRegistrations, inputActivators, typeof(WorldRoot), ServiceLifetime.Of<AterraWorld>(), typeof(SceneInput));

        // Act
        Action validateSiblingDependency = Validation(siblingActivators, parents: parents, registrations: siblingRegistrations);
        Action validateDescendantInput = Validation(inputActivators, inputs, parents, inputRegistrations);

        // Assert
        Check.Fails<DependencyInjectionException>(validateSiblingDependency, "Lifetime violation");
        Check.Fails<DependencyInjectionException>(validateDescendantInput, "Lifetime violation");
    }

    [Test]
    public void HandlesTenThousandDependencyNodesWithoutUsingTheCallStack() {
        // Arrange
        const int count = 10_000;
        Type[] services = UniqueTypes(count);
        var registrations = new Dictionary<Type, ServiceRegistration>(count);
        var activators = new Dictionary<Type, ServiceActivationPlan>(count);

        for (int index = 0; index < count; index++) {
            Type[] dependencies = index + 1 < count ? [services[index + 1]] : [];
            Add(registrations, activators, services[index], ServiceLifetime.Transient, dependencies);
        }

        // Act
        ServiceCollectionValidator.Validate(activators, new Dictionary<Type, Type>(), DefaultParents(), registrations);

        // Assert
        Check.True(registrations.Values.All(registration => registration.Activator is not null), "Not every activator was attached.");
    }

    private static void Add(
        Dictionary<Type, ServiceRegistration> registrations,
        Dictionary<Type, ServiceActivationPlan> activators,
        Type service,
        ServiceLifetime lifetime,
        params Type[] dependencies
    ) {
        var registration = new ServiceRegistration(new ServiceRecord(lifetime, service, service));
        var activator = new ServiceActivationPlan(Create: _ => null!, null, dependencies);
        registrations.Add(service, registration);
        activators.Add(service, activator);
    }

    private static Dictionary<Type, Type[]> DefaultParents() => new() {
        [typeof(AterraSingleton)] = [],
        [typeof(AterraHost)] = [typeof(AterraSingleton)],
        [typeof(AterraWorld)] = [typeof(AterraHost)],
        [typeof(AterraScene)] = [typeof(AterraWorld)]
    };

    private static Action Validation(
        IReadOnlyDictionary<Type, ServiceActivationPlan>? activators = null,
        IReadOnlyDictionary<Type, Type>? inputs = null,
        IReadOnlyDictionary<Type, Type[]>? parents = null,
        IReadOnlyDictionary<Type, ServiceRegistration>? registrations = null
    ) => () => ServiceCollectionValidator.Validate(
        activators ?? new Dictionary<Type, ServiceActivationPlan>(),
        inputs ?? new Dictionary<Type, Type>(),
        parents ?? DefaultParents(),
        registrations ?? new Dictionary<Type, ServiceRegistration>()
    );

    private static Action RegistrationValidation(Type service, Type implementation) {
        var registration = new ServiceRegistration(new ServiceRecord(ServiceLifetime.Host, service, implementation));
        return Validation(registrations: new Dictionary<Type, ServiceRegistration> { [service] = registration });
    }

    private static Type[] UniqueTypes(int count) {
        var result = new Type[count];
        for (int value = 0; value < count; value++) {
            Type type = typeof(TypeRoot);
            int remaining = value;
            do {
                type = (remaining & 1) == 0
                    ? typeof(Zero<>).MakeGenericType(type)
                    : typeof(One<>).MakeGenericType(type);
                remaining >>= 1;
            } while (remaining > 0);

            result[value] = type;
        }

        return result;
    }

    private sealed class LeftScope;

    private sealed class RightScope;

    private sealed class JoinedScope;

    private sealed class UnknownScope;

    // ReSharper disable once UnusedTypeParameter
    private sealed class GenericScope<T>;

    // ReSharper disable once UnusedTypeParameter
    private sealed class GenericInput<T>;

    // ReSharper disable once UnusedTypeParameter
    private sealed class GenericService<T>;

    // ReSharper disable once UnusedTypeParameter
    private sealed class GenericImplementation<T> : IContract;

    private sealed class WorldInput;

    private sealed class SceneInput;

    private interface IContract;

    private abstract class AbstractImplementation : IContract;

    private sealed class UnrelatedService;

    private sealed class ValidService;

    private sealed class FactoryService;

    private sealed class InstanceService : IContract;

    private interface IValueInstance;

    private sealed record ValueInstance(
        [UsedImplicitly]
        int Value
    ) : IValueInstance;

    private sealed class RootService;

    private sealed class MiddleService;

    private sealed class LeafService;

    private sealed class UnregisteredService;

    private sealed class WorldRoot;

    private sealed class HostRoot;

    private sealed class SharedTransient;

    private sealed class WorldDependency;

    private sealed class JoinedConsumer;

    private sealed class LeftScopedService;

    private sealed class TypeRoot;

    // ReSharper disable once UnusedTypeParameter
    private sealed class Zero<T>;

    // ReSharper disable once UnusedTypeParameter
    private sealed class One<T>;
}
