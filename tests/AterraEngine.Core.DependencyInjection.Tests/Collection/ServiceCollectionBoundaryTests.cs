// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using static AterraEngine.Core.DependencyInjection.Tests.Fixtures.TestFixtures;

namespace AterraEngine.Core.DependencyInjection.Tests.Collection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------

public sealed class ServiceCollectionBoundaryTests {
    [Test]
    public async Task FailedBuildDoesNotFreezeConfiguration() {
        // Arrange
        IServiceCollection services = new ServiceCollection().Add<BuildOnlyService>(ServiceLifetime.Host);

        // Act
        await Assert.That(() => services.Build()).ThrowsExactly<DependencyInjectionException>()
            .WithMessageContaining("No generated activator");

        services.AddActivator<BuildOnlyService>(_ => new BuildOnlyService());
        await using ServiceProvider provider = services.Build();

        // Assert
        await Assert.That(await provider.ResolveAsync<BuildOnlyService>()).IsNotNull();
    }

    [Test]
    public async Task ModuleFailureRestoresPreviousModuleContext() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        await Assert.That(() => services.AddModule("broken", configure: _ => throw new InvalidOperationException("module")))
            .ThrowsExactly<InvalidOperationException>().WithMessage("module");

        services.Add(new ServiceRecord(ServiceLifetime.Host, typeof(ModuleService), typeof(UnrelatedService)));

        // Assert
        var error = await Assert.That(() => services.Build()).ThrowsExactly<DependencyInjectionException>();
        await Assert.That(error!.Message).Contains("module: <application>");
        await Assert.That(error.Message).DoesNotContain("broken");
    }

    [Test]
    public async Task DuplicateActivatorsAndScopesAreRejectedWithoutReplacingTheOriginal() {
        // Arrange
        IServiceCollection services = new ServiceCollection()
            .AddActivator<ModuleService>(_ => new ModuleService())
            .DeclareScope<ChildScope>(typeof(AterraWorld));

        // Act
        await Assert.That(() => services.AddActivator<ModuleService>(_ => new ModuleService()))
            .ThrowsExactly<DependencyInjectionException>().WithMessageContaining("activator");
        await Assert.That(() => services.DeclareScope<ChildScope>(typeof(AterraHost)))
            .ThrowsExactly<DependencyInjectionException>().WithMessageContaining("already declared");

        await using ServiceProvider provider = services.Add<ModuleService>(ServiceLifetime.Host).Build();

        // Assert
        await Assert.That(await provider.ResolveAsync<ModuleService>()).IsNotNull();
    }

    [Test]
    public async Task ProviderGetReturnsRegisteredInputAndMissingValues() {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .RequireInput<AterraHost, HostInput>()
            .AddFactory<ModuleService>(ServiceLifetime.Host, factory: _ => new ModuleService())
            .Build(ServiceScopeInput.Of(new HostInput("host")));

        await Assert.That(provider.Get<ModuleService>()).IsNotNull();
        await Assert.That(provider.Get<HostInput>()).IsNotNull();
        await Assert.That(() => provider.Get<MissingService>()).Throws<DependencyInjectionException>();
    }

    [Test]
    public async Task FactoryResolverCannotBeUsedAfterFactoryReturns() {
        // Arrange
        IServiceResolver? captured = null;
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<ModuleService>(ServiceLifetime.Host, factory: resolver => {
                captured = resolver;
                return new ModuleService();
            })
            .Build();

        // Act
        await provider.ResolveAsync<ModuleService>();

        // Assert
        await Assert.That(() => captured!.Get<ModuleService>()).ThrowsExactly<InvalidOperationException>()
            .WithMessageContaining("only be used synchronously");
    }

    [Test]
    public async Task HostInputsAreValidatedBeforeProviderConstruction() {
        // Arrange
        IServiceCollection services = new ServiceCollection().RequireInput<AterraHost, HostInput>();

        // Act
        Func<Task> missing = async () => services.Build();
        Func<Task> wrong = async () => services.Build(ServiceScopeInput.Of(new WrongInput()));

        // Assert
        await Assert.That(missing).ThrowsExactly<DependencyInjectionException>()
            .WithMessageContaining("Required input");
        await Assert.That(wrong).ThrowsExactly<DependencyInjectionException>()
            .WithMessageContaining("not declared");
    }
}
