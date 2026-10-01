// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using static AterraEngine.Core.DependencyInjection.Tests.Fixtures.TestFixtures;

namespace AterraEngine.Core.DependencyInjection.Tests.Abstractions;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class ApiContractTests {
    [Test]
    public async Task CollectionRejectsNullAndInvalidArguments() {
        // Arrange
        var services = new ServiceCollection();

        // Act and assert
        await Assert.That(() => services.Add(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.AddActivator<ContractService>(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.AddActivator<ContractService>(create: _ => new ContractService(), null!))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.AddGeneratedActivator<ContractService>(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.AddFactory<ContractService>(ServiceLifetime.Host, null!))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.AddInstance<ContractService>(null!, ServiceInstanceOwnership.Caller))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.AddInstance(new ContractService(), (ServiceInstanceOwnership)99))
            .ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => services.DeclareScope<ChildScope>(null!)).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.DeclareScope<ChildScope>(typeof(AterraWorld), null!))
            .Throws<ArgumentException>();
        services.RequireInput<AterraWorld, ContractInput>();
        await Assert.That(services.RequireInput<AterraWorld, ContractInput>)
            .ThrowsExactly<DependencyInjectionException>();
    }

    [Test]
    public async Task ModuleRejectsInvalidArgumentsAndRestoresNestedContext() {
        // Arrange
        var services = new ServiceCollection();

        // Act and assert
        await Assert.That(() => services.AddModule(" ", configure: _ => {})).ThrowsExactly<ArgumentException>();
        await Assert.That(() => services.AddModule("valid", null!)).ThrowsExactly<ArgumentNullException>();

        // Arrange
        services.AddModule("outer", configure: outer => {
            outer.AddModule("inner", configure: inner => inner.AddFactory<ContractService>(ServiceLifetime.Host, factory: _ => new ContractService()));
            outer.AddFactory<SecondContractService>(ServiceLifetime.Host, factory: _ => new SecondContractService());
        });

        // Act
        await using ServiceProvider provider = services.Build();

        // Assert
        await Assert.That(await provider.ResolveAsync<ContractService>()).IsNotNull();
        await Assert.That(await provider.ResolveAsync<SecondContractService>()).IsNotNull();
    }

    [Test]
    public async Task ServiceScopeInputRejectsNullValues() {
        // Act and assert
        await Assert.That(() => ServiceScopeInput.Of<ContractInput>(null!))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new ServiceCollection().Build(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task CollectionRejectsNullInputAndAssemblyArguments() {
        // Arrange
        var services = new ServiceCollection();

        // Act and assert
        await Assert.That(() => services.RequireInput<AterraWorld, ServiceProvider>())
            .ThrowsExactly<DependencyInjectionException>().WithMessageContaining("built-in provider");
        await Assert.That(() => services.Add(new ServiceRecord(ServiceLifetime.Host, null!, typeof(ContractService))))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.Add(new ServiceRecord(ServiceLifetime.Host, typeof(ContractService), null!)))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => services.RegisterServicesFromAssembly(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task FailedFactoryActivationIsWrappedAndDoesNotPoisonProvider() {
        // Arrange
        IServiceCollection services = new ServiceCollection()
            .AddFactory<ContractService>(ServiceLifetime.Host, factory: _ => null!)
            .AddFactory<SecondContractService>(ServiceLifetime.Host, factory: _ => new SecondContractService());
        await using ServiceProvider provider = services.Build();

        // Act
        // ReSharper disable once AccessToDisposedClosure
        var error = await Assert.That(async () => await provider.ResolveAsync<ContractService>())
            .ThrowsExactly<DependencyInjectionException>();
        // Assert
        await Assert.That(error!.Message).Contains("Factory returned null");
        await Assert.That(await provider.ResolveAsync<SecondContractService>()).IsNotNull();
    }

    [Test]
    public async Task ResolverRejectsNullTypesAndUseOutsideFactoryThreadOrLifetime() {
        // Arrange
        IServiceResolver? resolver = null;
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<ContractService>(ServiceLifetime.Host, factory: supplied => {
                resolver = supplied;
                return new ContractService();
            })
            .Build();

        // Act
        await provider.ResolveAsync<ContractService>();

        // Assert
        await Assert.That(() => resolver!.Get<object>()).ThrowsExactly<InvalidOperationException>();
        Func<Task> crossThreadResolve = async () => await Task.Run(() => resolver!.Get<ContractService>());
        await Assert.That(crossThreadResolve)
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("synchronously");
        await Assert.That(() => resolver!.Get<ContractService>())
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("synchronously");
    }

    [Test]
    public async Task ScopeRetainsItsTypedProvider() {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection().Build();

        await Assert.That(provider.Host.Parent).IsSameReferenceAs(provider.Singleton);
        await Assert.That(provider.Host.ServiceProvider).IsSameReferenceAs(provider);
    }

    [Test]
    public async Task LifetimeFactoriesExposeExpectedScopeValues() {
        // Act and assert
        await Assert.That(ServiceLifetime.Transient.ScopeType).IsNull();
        await Assert.That(ServiceLifetime.Singleton.ScopeType).IsEqualTo(typeof(AterraSingleton));
        await Assert.That(ServiceLifetime.Host.ScopeType).IsEqualTo(typeof(AterraHost));
        await Assert.That(ServiceLifetime.Of<AterraWorld>().ScopeType).IsEqualTo(typeof(AterraWorld));
    }
}
