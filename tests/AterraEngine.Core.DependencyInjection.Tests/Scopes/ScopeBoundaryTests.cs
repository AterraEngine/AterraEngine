// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using static AterraEngine.Core.DependencyInjection.Tests.Fixtures.TestFixtures;

namespace AterraEngine.Core.DependencyInjection.Tests.Scopes;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class ScopeBoundaryTests {
    [Test]
    public async Task SingletonInputsCanBeResolvedFromEveryDescendant() {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .RequireInput<AterraSingleton, SingletonInput>()
            .AddFactory<SingletonConsumer>(ServiceLifetime.Singleton, factory: resolver =>
                new SingletonConsumer(resolver.Get<SingletonInput>()))
            .Build(ServiceScopeInput.Of(new SingletonInput("shared")));

        // Act
        var fromHost = await provider.ResolveAsync<SingletonConsumer>();
        OwnedServiceScope world = provider.CreateScope<AterraWorld>();
        var fromWorld = await world.ResolveAsync<SingletonConsumer>();

        // Assert
        await Assert.That(fromWorld).IsSameReferenceAs(fromHost);
        await Assert.That(fromHost.Input.Name).IsEqualTo("shared");
    }

    [Test]
    public async Task SameInputObjectCanBeBoundToIndependentScopesAndReleasedIndependently() {
        // Arrange
        var input = new WorldInput("shared");
        await using ServiceProvider provider = new ServiceCollection()
            .RequireInput<AterraWorld, WorldInput>()
            .AddFactory<InputConsumer>(ServiceLifetime.Of<AterraWorld>(), factory: resolver =>
                new InputConsumer(resolver.Get<WorldInput>()))
            .Build();
        OwnedServiceScope first = provider.CreateScope<AterraWorld>(ServiceScopeInput.Of(input));
        OwnedServiceScope second = provider.CreateScope<AterraWorld>(ServiceScopeInput.Of(input));

        // Act
        await Assert.That((await first.ResolveAsync<InputConsumer>()).Input).IsSameReferenceAs(input);
        await Assert.That((await second.ResolveAsync<InputConsumer>()).Input).IsSameReferenceAs(input);

        // Assert
        await first.DisposeAsync();
        await Assert.That((await second.ResolveAsync<InputConsumer>()).Input).IsSameReferenceAs(input);
        await second.DisposeAsync();
    }

    [Test]
    public async Task DuplicateInputsAreRejectedAtEachScopeBoundary() {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .RequireInput<AterraHost, HostInput>()
            .Build(ServiceScopeInput.Of(new HostInput("host")));

        // Act
        // ReSharper disable once AccessToDisposedClosure
        var wrongScope = await Assert.That(() => provider.CreateScope<AterraWorld>(
                ServiceScopeInput.Of(new WorldInput("wrong")),
                ServiceScopeInput.Of(new WorldInput("duplicate"))))
            .ThrowsExactly<DependencyInjectionException>();
        // Assert
        await Assert.That(wrongScope!.Message).Contains("not declared");

        IServiceCollection duplicateInputs = new ServiceCollection()
                .RequireInput<AterraHost, HostInput>()
            ;
        await Assert.That(() => duplicateInputs.Build(
                ServiceScopeInput.Of(new HostInput("one")), ServiceScopeInput.Of(new HostInput("two"))))
            .ThrowsExactly<DependencyInjectionException>().WithMessageContaining("Duplicate");
    }

    [Test]
    public async Task ContainerOwnedObjectsCannotAlsoBeCallerOwnedInputs() {
        // Arrange
        var shared = new ClaimedObject();
        await using ServiceProvider provider = new ServiceCollection()
            .AddInstance(shared, ServiceInstanceOwnership.Container)
            .RequireInput<AterraWorld, IClaimedInput>()
            .Build();

        // Act
        // ReSharper disable once AccessToDisposedClosure
        var ownershipError = await Assert.That(() => provider.CreateScope<AterraWorld>(
                ServiceScopeInput.Of<IClaimedInput>(shared)))
            .ThrowsExactly<DependencyInjectionException>();
        // Assert
        await Assert.That(ownershipError!.Message).Contains("container-owned");
    }

    [Test]
    public async Task MultiParentScopesCanBeCreatedFromEitherDeclaredParent() {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .DeclareScope<LeftScope>(typeof(AterraWorld))
            .DeclareScope<RightScope>(typeof(AterraWorld))
            .DeclareScope<JoinedScope>(typeof(LeftScope), typeof(RightScope))
            .AddFactory<JoinedService>(ServiceLifetime.Of<JoinedScope>(), factory: _ => new JoinedService())
            .Build();
        OwnedServiceScope world = provider.CreateScope<AterraWorld>();
        OwnedServiceScope left = world.CreateScope<LeftScope>();
        OwnedServiceScope right = world.CreateScope<RightScope>();
        OwnedServiceScope fromLeft = left.CreateScope<JoinedScope>();
        OwnedServiceScope fromRight = right.CreateScope<JoinedScope>();

        // Act
        await Assert.That(await fromLeft.ResolveAsync<JoinedService>()).IsNotNull();
        // Assert
        await Assert.That(await fromRight.ResolveAsync<JoinedService>()).IsNotNull();
        await Assert.That(fromLeft.Parent).IsSameReferenceAs(left);
        await Assert.That(fromRight.Parent).IsSameReferenceAs(right);
    }

    [Test]
    public async Task ProviderDisposeIsIdempotentAndClearsRegistrations() {
        // Arrange
        int calls = 0;
        ServiceProvider provider = new ServiceCollection()
            .AddFactory<DisposableService>(ServiceLifetime.Host, factory: _ => new DisposableService(() => calls++))
            .Build();
        // Act
        await provider.ResolveAsync<DisposableService>();

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        // Assert
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(async () => await provider.ResolveAsync<DisposableService>())
            .ThrowsExactly<ObjectDisposedException>();
        await Assert.That(() => provider.CreateScope<AterraWorld>())
            .ThrowsExactly<ObjectDisposedException>();
    }
}
