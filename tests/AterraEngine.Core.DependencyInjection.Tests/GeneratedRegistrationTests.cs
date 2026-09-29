// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Reflection;
using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection.Tests;
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
        OwnedScope world = host.CreateScope<World>();
        OwnedScope scene = world.CreateScope<Scene>();

        // Act
        IGeneratedClock firstClock = await host.ResolveAsync<IGeneratedClock>();
        IGeneratedClock secondClock = await host.ResolveAsync<IGeneratedClock>();
        GeneratedConsumer firstConsumer = await host.ResolveAsync<GeneratedConsumer>();
        GeneratedConsumer secondConsumer = await host.ResolveAsync<GeneratedConsumer>();
        GeneratedWorldService firstWorldService = await world.ResolveAsync<GeneratedWorldService>();
        GeneratedWorldService secondWorldService = await world.ResolveAsync<GeneratedWorldService>();
        GeneratedSceneService firstSceneService = await scene.ResolveAsync<GeneratedSceneService>();
        GeneratedSceneService secondSceneService = await scene.ResolveAsync<GeneratedSceneService>();

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
        IGeneratedMessage first = await host.ResolveAsync<IGeneratedMessage>();
        IGeneratedMessage second = await host.ResolveAsync<IGeneratedMessage>();

        // Assert
        Check.Different(first, second);
    }

    [Test]
    public async Task RegistrationsCanBeOverriddenBeforeBuild() {
        // Arrange
        var replacement = new ReplacementGeneratedClock();
        var services = new ServiceCollection().RegisterActivators<GeneratedRegistrationTests>()
            .AddInstance<IGeneratedClock>(replacement, ServiceInstanceOwnership.Caller);
        await using ServiceProvider host = services.Build();

        // Act
        IGeneratedClock resolved = await host.ResolveAsync<IGeneratedClock>();

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
