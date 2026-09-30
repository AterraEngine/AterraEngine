// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection.Tests;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public class ContainerTests {
    [Test]
    public async Task HostIsolationAndTransientIdentity() {
        // Arrange
        static IServiceCollection Configure() {
            return Services().Add<HostService>(ServiceLifetime.Host).Add<Helper>(ServiceLifetime.Transient);
        }

        await using ServiceProvider first = Configure().Build();
        await using ServiceProvider second = Configure().Build();

        // Act
        var service = await first.ResolveAsync<HostService>();

        // Assert
        Check.Same(service, await first.ResolveAsync<HostService>());
        Check.Different(service, await second.ResolveAsync<HostService>());
        Check.Different(await first.ResolveAsync<Helper>(), await first.ResolveAsync<Helper>());
    }

    [Test]
    public async Task SingletonIsOwnedAboveHostAndCanBeConsumedByHost() {
        // Arrange
        await using ServiceProvider provider = Services().Add<Helper>(ServiceLifetime.Singleton)
            .Add<MissingConsumer>(ServiceLifetime.Host).Build();
        OwnedServiceScope world = provider.CreateScope<AterraWorld>();
        OwnedServiceScope secondHost = provider.Singleton.CreateScope<AterraHost>();

        // Act
        var fromHost = await provider.ResolveAsync<Helper>();
        var fromWorld = await world.ResolveAsync<Helper>();
        var consumer = await provider.ResolveAsync<MissingConsumer>();
        var secondHostConsumer = await secondHost.ResolveAsync<MissingConsumer>();

        // Assert
        await Assert.That(provider.Singleton.ScopeType).IsEqualTo(typeof(AterraSingleton));
        Check.Same(provider.Singleton, provider.Host.Parent!);
        Check.Same(fromHost, fromWorld);
        Check.Same(fromHost, consumer.Helper);
        Check.Same(fromHost, secondHostConsumer.Helper);
        Check.Different(consumer, secondHostConsumer);
    }

    [Test]
    public void SingletonCannotDependOnHostService() {
        // Arrange
        IServiceCollection services = Services().Add<HostService>(ServiceLifetime.Host)
            .Add<BadSingleton>(ServiceLifetime.Singleton);

        // Act
        Action build = () => services.Build();

        // Assert
        Check.Fails<DependencyInjectionException>(build, "Lifetime violation");
    }

    [Test]
    public async Task WorldsShareAcrossSiblingScenesButRemainIndependent() {
        // Arrange
        await using ServiceProvider host = Services()
            .Add<WorldService>(ServiceLifetime.Of<AterraWorld>()).Add<SceneService>(ServiceLifetime.Of<AterraScene>()).Build();
        OwnedServiceScope worldA = host.CreateScope<AterraWorld>();
        OwnedServiceScope worldB = host.CreateScope<AterraWorld>();
        OwnedServiceScope sceneA = worldA.CreateScope<AterraScene>();
        OwnedServiceScope sceneB = worldA.CreateScope<AterraScene>();
        OwnedServiceScope sceneC = worldB.CreateScope<AterraScene>();

        // Act
        var worldServiceA = await sceneA.ResolveAsync<WorldService>();
        var sceneServiceA = await sceneA.ResolveAsync<SceneService>();

        // Assert
        Check.Same(worldServiceA, await sceneB.ResolveAsync<WorldService>());
        Check.Different(worldServiceA, await sceneC.ResolveAsync<WorldService>());
        Check.Same(sceneServiceA, await sceneA.ResolveAsync<SceneService>());
        Check.Different(sceneServiceA, await sceneB.ResolveAsync<SceneService>());
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<WorldService>().AsTask(), "Missing ownership scope");
        await Check.FailsAsync<DependencyInjectionException>(action: () => worldA.ResolveAsync<SceneService>().AsTask(), "Missing ownership scope");
    }

    [Test]
    public async Task InputsAreTypedRequiredIndependentAndAnchored() {
        // Arrange
        IServiceCollection collection = Services().RequireInput<AterraWorld, WorldConfig>()
            .RequireInput<AterraScene, SceneConfig>().Add<ConfiguredWorld>(ServiceLifetime.Of<AterraWorld>());
        ServiceProvider host = collection.Build();
        await using ServiceProvider cleanup = host;

        // Act
        OwnedServiceScope a = host.CreateScope<AterraWorld>(ServiceScopeInput.Of(new WorldConfig(10)));
        OwnedServiceScope b = host.CreateScope<AterraWorld>(ServiceScopeInput.Of(new WorldConfig(20)));
        OwnedServiceScope scene = a.CreateScope<AterraScene>(ServiceScopeInput.Of(new SceneConfig("scene")));

        // Assert
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<AterraWorld>(), "Required input");
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<AterraWorld>(ServiceScopeInput.Of(new SceneConfig("wrong"))), "not declared");
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<AterraWorld>(ServiceScopeInput.Of(new WorldConfig(1)), ServiceScopeInput.Of(new WorldConfig(2))), "Duplicate input");
        await Assert.That((await scene.ResolveAsync<ConfiguredWorld>()).Config.Seed).IsEqualTo(10);
        await Assert.That((await b.ResolveAsync<ConfiguredWorld>()).Config.Seed).IsEqualTo(20);
        Check.Same(await scene.ResolveAsync<ConfiguredWorld>(), await a.ResolveAsync<ConfiguredWorld>());
        await Check.FailsAsync<DependencyInjectionException>(action: () => a.ResolveAsync<SceneConfig>().AsTask(), "Missing ownership scope");
    }

    [Test]
    public async Task OpaqueFactoryCannotCaptureRequestingSceneOrItsInput() {
        // Arrange
        await using ServiceProvider host = Services().RequireInput<AterraScene, SceneConfig>()
            .Add<SceneService>(ServiceLifetime.Of<AterraScene>())
            .AddFactory<WorldService>(ServiceLifetime.Of<AterraWorld>(), factory: r => {
                r.Get<SceneService>();
                return new WorldService();
            })
            .AddFactory<ConfiguredWorld>(ServiceLifetime.Of<AterraWorld>(), factory: r => {
                r.Get<SceneConfig>();
                return new ConfiguredWorld(new WorldConfig(0));
            }).Build();
        OwnedServiceScope scene = host.CreateScope<AterraWorld>().CreateScope<AterraScene>(ServiceScopeInput.Of(new SceneConfig("local")));

        // Act
        Func<Task> resolveService = () => scene.ResolveAsync<WorldService>().AsTask();
        Func<Task> resolveInput = () => scene.ResolveAsync<ConfiguredWorld>().AsTask();

        // Assert
        await Check.FailsAsync<DependencyInjectionException>(resolveService, "resolving from AterraWorld");
        await Check.FailsAsync<DependencyInjectionException>(resolveInput, "resolving from AterraWorld");
    }

    [Test]
    public void BuildRejectsDirectAndTransitiveLifetimeViolations() {
        // Arrange
        IServiceCollection direct = Services().Add<WorldService>(ServiceLifetime.Of<AterraWorld>()).Add<BadHost>(ServiceLifetime.Host);
        IServiceCollection transitive = Services().Add<WorldService>(ServiceLifetime.Of<AterraWorld>()).Add<WorldHelper>(ServiceLifetime.Transient)
            .Add<IndirectBadHost>(ServiceLifetime.Host);
        IServiceCollection input = Services().RequireInput<AterraScene, SceneConfig>().Add<BadInputWorld>(ServiceLifetime.Of<AterraWorld>());

        // Act
        Action buildDirect = () => direct.Build();
        Action buildTransitive = () => transitive.Build();
        Action buildInput = () => input.Build();

        // Assert
        Check.Fails<DependencyInjectionException>(buildDirect, "Lifetime violation");
        Check.Fails<DependencyInjectionException>(buildTransitive, "Lifetime violation");
        Check.Fails<DependencyInjectionException>(buildInput, "Lifetime violation");
    }

    [Test]
    public async Task RuntimeLifetimeCheckIncludesTransientFactoryDependencies() {
        // Arrange
        await using ServiceProvider host = Services().Add<WorldService>(ServiceLifetime.Of<AterraWorld>())
            .AddFactory<WorldHelper>(ServiceLifetime.Transient, factory: r => new WorldHelper(r.Get<WorldService>()))
            .Add<IndirectBadHost>(ServiceLifetime.Host).Build();
        OwnedServiceScope scene = host.CreateScope<AterraWorld>().CreateScope<AterraScene>();

        // Act
        Func<Task> resolve = () => scene.ResolveAsync<IndirectBadHost>().AsTask();

        // Assert
        await Check.FailsAsync<DependencyInjectionException>(resolve, "resolving from AterraHost");
    }

    [Test]
    public void BuildValidatesWithoutRunningConstructorsOrFactories() {
        // Arrange
        int calls = 0;
        Action buildMissingDependency = () => Services()
            .AddFactory<HostService>(ServiceLifetime.Host, factory: _ => {
                calls++;
                return new HostService();
            })
            .AddModule("broken-module", configure: c => c.Add<MissingConsumer>(ServiceLifetime.Host)).Build();
        Action buildCycle = () => Services().Add<CycleA>(ServiceLifetime.Host).Add<CycleB>(ServiceLifetime.Host).Build();
        Action buildAmbiguous = () => Services().Add<Ambiguous>(ServiceLifetime.Host).Build();
        Action buildWrongImplementation = () => new ServiceCollection()
            .Add(new ServiceRecord(ServiceLifetime.Host, typeof(HostService), typeof(WorldService))).Build();
        Action buildAbstract = () => new ServiceCollection().Add<AbstractService>(ServiceLifetime.Host).Build();

        // Act
        Check.Fails<DependencyInjectionException>(buildMissingDependency, "broken-module");

        // Assert
        Check.True(calls == 0, "Build invoked a factory.");
        Check.Fails<DependencyInjectionException>(buildCycle, "cycle");
        Check.Fails<DependencyInjectionException>(buildAmbiguous, "No generated activator");
        Check.Fails<DependencyInjectionException>(buildWrongImplementation, "Invalid implementation");
        Check.Fails<DependencyInjectionException>(buildAbstract, "Invalid implementation");
    }

    [Test]
    public async Task LaterModulesOverrideServicesAndBuildFreezesConfiguration() {
        // Arrange
        IServiceCollection collection = Services()
            .AddModule("core", configure: c => c.Add<IPluginService, DefaultPluginService>(ServiceLifetime.Host))
            .AddModule("plugin", configure: c => c.Add<IPluginService, ReplacementPluginService>(ServiceLifetime.Transient));

        // Act
        await using ServiceProvider host = collection.Build();
        var first = await host.ResolveAsync<IPluginService>();
        var second = await host.ResolveAsync<IPluginService>();

        // Assert
        await Assert.That(first).IsTypeOf<ReplacementPluginService>();
        Check.Different(first, second);
        Check.Fails<InvalidOperationException>(action: () => collection.Add<Helper>(ServiceLifetime.Transient), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.DeclareScope<CustomScope>(typeof(AterraHost)), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.RequireInput<AterraWorld, WorldConfig>(), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.Build(), "immutable");
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<Helper>().AsTask(), "Unregistered");
    }

    [Test]
    public async Task ExtensibleScopesValidateParentRelationships() {
        // Arrange
        ServiceProvider host = Services().DeclareScope<CustomScope>(typeof(AterraWorld))
            .Add<Helper>(ServiceLifetime.Of<CustomScope>()).Build();
        await using ServiceProvider cleanup = host;
        OwnedServiceScope world = host.CreateScope<AterraWorld>();
        OwnedServiceScope custom = world.CreateScope<CustomScope>();

        // Act
        var helper = await custom.ResolveAsync<Helper>();

        // Assert
        Check.Same(helper, await custom.ResolveAsync<Helper>());
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<AterraScene>(), "cannot be created");
        Check.Fails<DependencyInjectionException>(action: () => world.CreateScope<AterraWorld>(), "cannot be created");
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<CustomScope>(), "cannot be created");
        Check.Fails<DependencyInjectionException>(action: () => custom.CreateScope<UnknownScope>(), "cannot be created");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().DeclareScope<CustomScope>(typeof(UnknownScope)).Build(), "Undeclared");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().DeclareScope<CustomScope>(typeof(CustomScope)).Build(), "cycle");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().Add<Helper>(ServiceLifetime.Of<UnknownScope>()).Build(), "Undeclared");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().DeclareScope<CustomScope>().Build(), "path to AterraSingleton");
        Check.Fails<DependencyInjectionException>(action: () => Services().DeclareScope<CustomScope>(typeof(AterraHost), typeof(AterraWorld))
            .Add<WorldService>(ServiceLifetime.Of<AterraWorld>()).Add<WorldHelper>(ServiceLifetime.Of<CustomScope>()).Build(), "Lifetime violation");
    }

    [Test]
    public void InputsCannotOverrideServices() {
        // Arrange
        var serviceFirst = new ServiceCollection();
        var inputFirst = new ServiceCollection();

        // Act
        Action addInput = () => serviceFirst.Add<WorldService>(ServiceLifetime.Host).RequireInput<AterraWorld, WorldService>();
        Action addService = () => inputFirst.RequireInput<AterraWorld, WorldService>().Add<WorldService>(ServiceLifetime.Host);

        // Assert
        Check.Fails<DependencyInjectionException>(addInput, "conflicts");
        Check.Fails<DependencyInjectionException>(addService, "Conflicts");
    }

    [Test]
    public async Task LegacyServiceRecordMapsToTypedLifetimes() {
        // Arrange
        var record = new ServiceRecord(ServiceScope.Singleton, typeof(HostService), typeof(HostService));
        await using ServiceProvider host = Services().Add(record).Build();

        // Act
        var service = await host.ResolveAsync<HostService>();
        var serviceFromWorld = await host.CreateScope<AterraWorld>().ResolveAsync<HostService>();

        // Assert
        await Assert.That(record.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        Check.Same(service, serviceFromWorld);
    }

    [Test]
    public async Task ExplicitActivatorRetainsRuntimeResolverSupport() {
        // Arrange
        var services = new ServiceCollection();
        services.AddActivator<WorldService>(_ => new WorldService());
        services.AddActivator<WorldHelper>(resolver => new WorldHelper(resolver.Get<WorldService>()), typeof(WorldService));
        await using ServiceProvider host = services.Add<WorldService>(ServiceLifetime.Host).Add<WorldHelper>(ServiceLifetime.Transient).Build();

        // Act
        var helper = await host.ResolveAsync<WorldHelper>();
        var service = await host.ResolveAsync<WorldService>();

        // Assert
        Check.Same(service, helper.World);
    }

    [Test]
    public async Task CachedExceptionInstanceIsAServiceValueRatherThanFailure() {
        // Arrange
        var expected = new InvalidOperationException("service-value");
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<Exception>(ServiceLifetime.Host, _ => expected).Build();

        // Act
        var first = await provider.ResolveAsync<Exception>();
        var second = await provider.ResolveAsync<Exception>();

        // Assert
        Check.Same(expected, first);
        Check.Same(first, second);
    }

    [Test]
    public async Task GeneratedServicesCanDependOnTheBuiltInProviderServices() {
        // Arrange
        await using ServiceProvider host = Services().Add<HostService>(ServiceLifetime.Host)
            .Add<ProviderConsumer>(ServiceLifetime.Host).Build();

        // Act
        var consumer = await host.ResolveAsync<ProviderConsumer>();
        var abstraction = await host.ResolveAsync<IServiceProvider>();
        object? service = abstraction.GetService(typeof(HostService));
        object? missing = abstraction.GetService(typeof(UnregisteredService));

        // Assert
        Check.Same(host, consumer.Provider);
        Check.Same(host, consumer.ConcreteProvider);
        Check.Same(host, abstraction);
        Check.Same(await host.ResolveAsync<HostService>(), service!);
        await Assert.That(missing).IsNull();
    }

    [Test]
    public void BuiltInProviderServicesCannotBeOverridden() {
        // Arrange
        var replacement = new StubProvider();

        // Act
        Action registerInterface = () => new ServiceCollection().AddInstance<IServiceProvider>(replacement, ServiceInstanceOwnership.Caller);
        Action registerConcrete = () => new ServiceCollection().Add<ServiceProvider>(ServiceLifetime.Host);
        Action declareInput = () => new ServiceCollection().RequireInput<AterraWorld, IServiceProvider>();

        // Assert
        Check.Fails<DependencyInjectionException>(registerInterface, "built-in provider");
        Check.Fails<DependencyInjectionException>(registerConcrete, "built-in provider");
        Check.Fails<DependencyInjectionException>(declareInput, "built-in provider");
    }

    private static ServiceCollection Services() {
        var services = new ServiceCollection();
        services.AddActivator<HostService>(_ => new HostService())
            .AddActivator<WorldService>(_ => new WorldService())
            .AddActivator<SceneService>(_ => new SceneService())
            .AddActivator<Helper>(_ => new Helper())
            .AddActivator<ConfiguredWorld>(resolver => new ConfiguredWorld(resolver.Get<WorldConfig>()), typeof(WorldConfig))
            .AddActivator<ProviderConsumer>(resolver => new ProviderConsumer(resolver.Get<IServiceProvider>(), resolver.Get<ServiceProvider>()),
                typeof(IServiceProvider), typeof(ServiceProvider))
            .AddActivator<DefaultPluginService>(_ => new DefaultPluginService())
            .AddActivator<ReplacementPluginService>(_ => new ReplacementPluginService())
            .AddActivator<BadHost>(resolver => new BadHost(resolver.Get<WorldService>()), typeof(WorldService))
            .AddActivator<BadSingleton>(resolver => new BadSingleton(resolver.Get<HostService>()), typeof(HostService))
            .AddActivator<WorldHelper>(resolver => new WorldHelper(resolver.Get<WorldService>()), typeof(WorldService))
            .AddActivator<IndirectBadHost>(resolver => new IndirectBadHost(resolver.Get<WorldHelper>()), typeof(WorldHelper))
            .AddActivator<BadInputWorld>(resolver => new BadInputWorld(resolver.Get<SceneConfig>()), typeof(SceneConfig))
            .AddActivator<MissingConsumer>(resolver => new MissingConsumer(resolver.Get<Helper>()), typeof(Helper))
            .AddActivator<CycleA>(resolver => new CycleA(resolver.Get<CycleB>()), typeof(CycleB))
            .AddActivator<CycleB>(resolver => new CycleB(resolver.Get<CycleA>()), typeof(CycleA));
        return services;
    }

    public sealed class HostService;

    public sealed class WorldService;

    public sealed class SceneService;

    public sealed class Helper;

    public interface IPluginService;

    public sealed class DefaultPluginService : IPluginService;

    public sealed class ReplacementPluginService : IPluginService;

    public sealed class ProviderConsumer(IServiceProvider provider, ServiceProvider concreteProvider) {
        public IServiceProvider Provider { get; } = provider;
        public ServiceProvider ConcreteProvider { get; } = concreteProvider;
    }

    public sealed record WorldConfig(int Seed);

    public sealed class SceneConfig {
        public SceneConfig(string name) => ArgumentException.ThrowIfNullOrEmpty(name);
    }

    public sealed class ConfiguredWorld(WorldConfig config) {
        public WorldConfig Config { get; } = config;
    }

    public sealed class BadHost(WorldService world) {
        public WorldService World { get; } = world;
    }

    public sealed class BadSingleton(HostService host) {
        public HostService Host { get; } = host;
    }

    public sealed class WorldHelper(WorldService world) {
        public WorldService World { get; } = world;
    }

    public sealed class IndirectBadHost(WorldHelper helper) {
        public WorldHelper Dependency { get; } = helper;
    }

    public sealed class BadInputWorld(SceneConfig config) {
        public SceneConfig Config { get; } = config;
    }

    public sealed class MissingConsumer(Helper helper) {
        public Helper Helper { get; } = helper;
    }

    public sealed class CycleA(CycleB b) {
        public CycleB B { get; } = b;
    }

    public sealed class CycleB(CycleA a) {
        public CycleA A { get; } = a;
    }

    public sealed class Ambiguous {
        public Ambiguous() {}
        public Ambiguous(Helper helper) => _ = helper;
    }

    public abstract class AbstractService;

    public sealed class CustomScope;

    public sealed class UnknownScope;

    private sealed class UnregisteredService;

    private sealed class StubProvider : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }
}

internal static class Check {
    internal static void True(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }
    internal static void Same(object expected, object actual) => True(ReferenceEquals(expected, actual), "Expected the same instance.");
    internal static void Different(object first, object second) => True(!ReferenceEquals(first, second), "Expected different instances.");
    internal static T Fails<T>(Action action, string text = "") where T : Exception {
        try { action(); }
        catch (T exception) {
            True(exception.ToString().Contains(text, StringComparison.OrdinalIgnoreCase), $"Expected '{text}' in {exception}.");
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    internal static async Task<T> FailsAsync<T>(Func<Task> action, string text = "") where T : Exception {
        try { await action(); }
        catch (T exception) {
            True(exception.ToString().Contains(text, StringComparison.OrdinalIgnoreCase), $"Expected '{text}' in {exception}.");
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
