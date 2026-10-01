// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using AterraEngine.Core.DependencyInjection.Tests.Generation;
using static AterraEngine.Core.DependencyInjection.Tests.Fixtures.TestFixtures;

namespace AterraEngine.Core.DependencyInjection.Tests.Collection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class MutationAndFailureTests {
    [Test]
    public async Task EveryMutationApiRejectsChangesAfterBuild() {
        // Arrange
        var services = new ServiceCollection();
        await using ServiceProvider provider = services.Build();

        // Act and assert
        await Assert.That(() => services.Add<MutationService>(ServiceLifetime.Host))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.AddActivator<MutationService>(_ => new MutationService()))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.AddGeneratedActivator<MutationService>(static (ref _) => new MutationService()))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.AddFactory<MutationService>(ServiceLifetime.Host, factory: _ => new MutationService()))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.AddInstance(new MutationService(), ServiceInstanceOwnership.Caller))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.AddModule("late", configure: _ => {}))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.RegisterServicesFromAssembly<GeneratedRegistrationTests>())
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.DeclareScope<MutationScope>(typeof(AterraWorld)))
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.RequireInput<AterraWorld, MutationInput>())
            .ThrowsExactly<InvalidOperationException>().WithMessageContaining("immutable");
        await Assert.That(() => services.Build()).ThrowsExactly<InvalidOperationException>()
            .WithMessageContaining("immutable");
        await Assert.That(provider).IsNotNull();
    }

    [Test]
    public async Task BuildCannotRunInsideModuleContributionAndModuleStateIsRestored() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        await Assert.That(() => services.AddModule("building", configure: collection => collection.Build()))
            .ThrowsExactly<DependencyInjectionException>().WithMessageContaining("inside a module");

        // Assert
        services.AddFactory<MutationService>(ServiceLifetime.Host, factory: _ => new MutationService());
        await using ServiceProvider provider = services.Build();
        await Assert.That(await provider.ResolveAsync<MutationService>()).IsNotNull();
    }

    [Test]
    public async Task FactoryExceptionPreservesOriginalExceptionAsInnerCause() {
        // Arrange
        var cause = new InvalidOperationException("root-cause");
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<MutationService>(ServiceLifetime.Host, factory: _ => throw cause)
            .Build();

        // Act
        // ReSharper disable once AccessToDisposedClosure
        var error = await Assert.That(async () => await provider.ResolveAsync<MutationService>())
            .ThrowsExactly<DependencyInjectionException>();

        // Assert
        await Assert.That(error!.InnerException).IsSameReferenceAs(cause);
        await Assert.That(error.Message).Contains("Activation failed");
    }

    [Test]
    public async Task UnregisteredAndWrongScopeResolutionsReportTheirBoundary() {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .DeclareScope<MutationScope>(typeof(AterraWorld))
            .AddFactory<MutationService>(ServiceLifetime.Of<MutationScope>(), factory: _ => new MutationService())
            .Build();

        // Act
        // ReSharper disable once AccessToDisposedClosure
        var unregistered = await Assert.That(async () => await provider.ResolveAsync<MissingMutationService>())
            .ThrowsExactly<DependencyInjectionException>();

        // Assert
        await Assert.That(unregistered!.Message).Contains("Unregistered service");
        // ReSharper disable once AccessToDisposedClosure
        var missingScope = await Assert.That(async () => await provider.ResolveAsync<MutationService>())
            .ThrowsExactly<DependencyInjectionException>();
        await Assert.That(missingScope!.Message).Contains("Missing ownership scope");
    }

    [Test]
    public async Task ScopeDisposalRemovesItFromParentAndAllowsIndependentSiblingTeardown() {
        // Arrange
        int firstDisposed = 0;
        int secondDisposed = 0;
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<FirstMutationService>(ServiceLifetime.Of<AterraWorld>(), factory: _ =>
                new FirstMutationService(() => firstDisposed++))
            .AddFactory<SecondMutationService>(ServiceLifetime.Of<AterraWorld>(), factory: _ =>
                new SecondMutationService(() => secondDisposed++))
            .Build();
        OwnedServiceScope first = provider.CreateScope<AterraWorld>();
        OwnedServiceScope second = provider.CreateScope<AterraWorld>();
        // Act
        await first.ResolveAsync<FirstMutationService>();
        await second.ResolveAsync<SecondMutationService>();

        await first.DisposeAsync();
        // Assert
        await Assert.That(firstDisposed).IsEqualTo(1);
        await Assert.That(secondDisposed).IsEqualTo(0);
        await second.DisposeAsync();
        await Assert.That(secondDisposed).IsEqualTo(1);
        await Assert.That(async () => await first.ResolveAsync<FirstMutationService>())
            .ThrowsExactly<ObjectDisposedException>();
    }
}
