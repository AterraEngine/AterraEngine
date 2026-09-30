// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Reflection;
using AterraEngine.Core.DependencyInjection.Tests.Fixtures;

namespace AterraEngine.Core.DependencyInjection.Tests.Generation;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public class GeneratedRegistrationTests {
    [Test]
    public async Task GenericAssemblyRegistrationAppliesLifetimesAndSelectedConstructor() {
        // Arrange
        await using ServiceProvider host = new ServiceCollection()
            .RegisterActivators<GeneratedRegistrationTests>()
            .Build();
        OwnedServiceScope world = host.CreateScope<AterraWorld>();
        OwnedServiceScope scene = world.CreateScope<AterraScene>();

        // Act
        var firstClock = await host.ResolveAsync<IGeneratedClock>();
        var secondClock = await host.ResolveAsync<IGeneratedClock>();
        var firstConsumer = await host.ResolveAsync<GeneratedConsumer>();
        var secondConsumer = await host.ResolveAsync<GeneratedConsumer>();
        var firstWorldService = await world.ResolveAsync<GeneratedWorldService>();
        var secondWorldService = await world.ResolveAsync<GeneratedWorldService>();
        var firstSceneService = await scene.ResolveAsync<GeneratedSceneService>();
        var secondSceneService = await scene.ResolveAsync<GeneratedSceneService>();

        // Assert
        Check.Same(firstClock, secondClock);
        Check.True(host.Singleton.Cache.ContainsKey(typeof(IGeneratedClock)), "The singleton should be cached above Host.");
        Check.True(!host.Host.Cache.ContainsKey(typeof(IGeneratedClock)), "The singleton should not use the Host cache.");
        Check.Different(firstConsumer, secondConsumer);
        Check.Same(firstClock, firstConsumer.Clock);
        Check.True(firstConsumer.UsedServiceConstructor, "The marked constructor should be selected.");
        Check.Same(firstWorldService, secondWorldService);
        Check.Same(firstSceneService, secondSceneService);
    }

    [Test]
    public async Task AssemblyOverloadRegistersGeneralServiceAttribute() {
        // Arrange
        Assembly assembly = typeof(GeneratedRegistrationTests).Assembly;
        await using ServiceProvider host = new ServiceCollection().RegisterActivators(assembly).Build();

        // Act
        var first = await host.ResolveAsync<IGeneratedMessage>();
        var second = await host.ResolveAsync<IGeneratedMessage>();

        // Assert
        Check.Different(first, second);
    }

    [Test]
    public async Task RegistrationsCanBeOverriddenBeforeBuild() {
        // Arrange
        var replacement = new ReplacementGeneratedClock();
        IServiceCollection services = new ServiceCollection().RegisterActivators<GeneratedRegistrationTests>()
            .AddInstance<IGeneratedClock>(replacement, ServiceInstanceOwnership.Caller);
        await using ServiceProvider host = services.Build();

        // Act
        var resolved = await host.ResolveAsync<IGeneratedClock>();

        // Assert
        Check.Same(replacement, resolved);
    }

    [Test]
    public void AssemblyWithoutGeneratedServicesIsRejected() {
        // Arrange
        var services = new ServiceCollection();

        // Act
        Action register = () => services.RegisterActivators(typeof(ServiceCollection).Assembly);

        // Assert
        Check.Fails<DependencyInjectionException>(register, "no generated service registrations");
    }
}

public interface IGeneratedClock;

[SingletonService<IGeneratedClock>]
public sealed class GeneratedClock : IGeneratedClock;

[TransientService<GeneratedConsumer>]
public sealed class GeneratedConsumer {
    public GeneratedConsumer() {
        Clock = null!;
    }

    [ServiceConstructor]
    public GeneratedConsumer(IGeneratedClock clock) {
        Clock = clock;
        UsedServiceConstructor = true;
    }

    public IGeneratedClock Clock { get; }
    public bool UsedServiceConstructor { get; }
}

[WorldService<GeneratedWorldService>]
public sealed class GeneratedWorldService;

[SceneService<GeneratedSceneService>]
public sealed class GeneratedSceneService;

public interface IGeneratedMessage;

[Service<IGeneratedMessage>(ServiceScope.Transient)]
public sealed class GeneratedMessage : IGeneratedMessage;

public sealed class ReplacementGeneratedClock : IGeneratedClock;
