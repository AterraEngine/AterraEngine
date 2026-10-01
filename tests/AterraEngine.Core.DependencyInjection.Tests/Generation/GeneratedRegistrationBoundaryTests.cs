// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection.Tests.Generation;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class GeneratedRegistrationBoundaryTests {
    [Test]
    public async Task RegisterAssemblyRejectsNullArgumentsAndDuplicateAssemblyRegistration() {
        // Act and assert
        await Assert.That(() => GeneratedServiceRegistration.RegisterAssembly(null!, register: _ => {}))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => GeneratedServiceRegistration.RegisterAssembly(typeof(GeneratedRegistrationBoundaryTests).Assembly, null!))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => GeneratedServiceRegistration.RegisterAssembly(
                typeof(GeneratedRegistrationTests).Assembly, register: _ => {}))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("already registered");
    }

    [Test]
    public async Task RegisteringGeneratedAssemblyTwiceOnOneCollectionFailsAtActivatorInstallation() {
        // Arrange
        IServiceCollection services = new ServiceCollection().RegisterServicesFromAssembly<GeneratedRegistrationTests>();

        // Act and assert
        await Assert.That(() => services.RegisterServicesFromAssembly<GeneratedRegistrationTests>())
            .ThrowsExactly<DependencyInjectionException>().WithMessageContaining("activator");
    }

    [Test]
    public async Task AssemblyRegistrationCallbackCanBeAppliedOnlyToMutableCollections() {
        // Arrange
        IServiceCollection services = new ServiceCollection().RegisterServicesFromAssembly<GeneratedRegistrationTests>();
        await using ServiceProvider provider = services.Build();

        // Act and assert
        await Assert.That(() => services.RegisterServicesFromAssembly(typeof(GeneratedRegistrationTests).Assembly))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(provider).IsNotNull();
    }

    [Test]
    public async Task AssemblyOverloadUsesTheSameGeneratedRegistrationAsTheMarkerOverload() {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .RegisterServicesFromAssembly(typeof(GeneratedRegistrationTests).Assembly)
            .Build();

        // Act
        var message = await provider.ResolveAsync<IGeneratedMessage>();

        // Assert
        await Assert.That(message).IsTypeOf<GeneratedMessage>();
    }
}
