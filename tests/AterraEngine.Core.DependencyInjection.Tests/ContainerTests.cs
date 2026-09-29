using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection.Tests;
public class ContainerTests {
    [Test]
    public async Task HostIsolationAndTransientIdentity() {
        // Arrange
        static ServiceCollection Configure() {
            return Services().Add<HostService>(Lifetime.Host).Add<Helper>(Lifetime.Transient);
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
    public async Task WorldsShareAcrossSiblingScenesButRemainIndependent() {
        // Arrange
        await using ServiceProvider host = Services()
            .Add<WorldService>(Lifetime.Of<World>()).Add<SceneService>(Lifetime.Of<Scene>()).Build();
        OwnedScope worldA = host.CreateScope<World>();
        OwnedScope worldB = host.CreateScope<World>();
        OwnedScope sceneA = worldA.CreateScope<Scene>();
        OwnedScope sceneB = worldA.CreateScope<Scene>();
        OwnedScope sceneC = worldB.CreateScope<Scene>();

        // Act
        WorldService worldServiceA = await sceneA.ResolveAsync<WorldService>();
        SceneService sceneServiceA = await sceneA.ResolveAsync<SceneService>();

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
        ServiceCollection collection = Services().RequireInput<World, WorldConfig>()
            .RequireInput<Scene, SceneConfig>().Add<ConfiguredWorld>(Lifetime.Of<World>());
        await using ServiceProvider host = collection.Build();

        // Act
        OwnedScope a = host.CreateScope<World>(ScopeInput.Of(new WorldConfig(10)));
        OwnedScope b = host.CreateScope<World>(ScopeInput.Of(new WorldConfig(20)));
        OwnedScope scene = a.CreateScope<Scene>(ScopeInput.Of(new SceneConfig("scene")));

        // Assert
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<World>(), "Required input");
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<World>(ScopeInput.Of(new SceneConfig("wrong"))), "not declared");
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<World>(ScopeInput.Of(new WorldConfig(1)), ScopeInput.Of(new WorldConfig(2))), "Duplicate input");
        await Assert.That((await scene.ResolveAsync<ConfiguredWorld>()).Config.Seed).IsEqualTo(10);
        await Assert.That((await b.ResolveAsync<ConfiguredWorld>()).Config.Seed).IsEqualTo(20);
        Check.Same(await scene.ResolveAsync<ConfiguredWorld>(), await a.ResolveAsync<ConfiguredWorld>());
        await Check.FailsAsync<DependencyInjectionException>(action: () => a.ResolveAsync<SceneConfig>().AsTask(), "Missing ownership scope");
    }

    [Test]
    public async Task OpaqueFactoryCannotCaptureRequestingSceneOrItsInput() {
        // Arrange
        await using ServiceProvider host = Services().RequireInput<Scene, SceneConfig>()
            .Add<SceneService>(Lifetime.Of<Scene>())
            .AddFactory<WorldService>(Lifetime.Of<World>(), factory: r => {
                r.Get<SceneService>();
                return new WorldService();
            })
            .AddFactory<ConfiguredWorld>(Lifetime.Of<World>(), factory: r => {
                r.Get<SceneConfig>();
                return new ConfiguredWorld(new WorldConfig(0));
            }).Build();
        OwnedScope scene = host.CreateScope<World>().CreateScope<Scene>(ScopeInput.Of(new SceneConfig("local")));

        // Act
        Func<Task> resolveService = () => scene.ResolveAsync<WorldService>().AsTask();
        Func<Task> resolveInput = () => scene.ResolveAsync<ConfiguredWorld>().AsTask();

        // Assert
        await Check.FailsAsync<DependencyInjectionException>(resolveService, "resolving from World");
        await Check.FailsAsync<DependencyInjectionException>(resolveInput, "resolving from World");
    }

    [Test]
    public void BuildRejectsDirectAndTransitiveLifetimeViolations() {
        // Arrange
        ServiceCollection direct = Services().Add<WorldService>(Lifetime.Of<World>()).Add<BadHost>(Lifetime.Host);
        ServiceCollection transitive = Services().Add<WorldService>(Lifetime.Of<World>()).Add<WorldHelper>(Lifetime.Transient)
            .Add<IndirectBadHost>(Lifetime.Host);
        ServiceCollection input = Services().RequireInput<Scene, SceneConfig>().Add<BadInputWorld>(Lifetime.Of<World>());

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
        await using ServiceProvider host = Services().Add<WorldService>(Lifetime.Of<World>())
            .AddFactory<WorldHelper>(Lifetime.Transient, factory: r => new WorldHelper(r.Get<WorldService>()))
            .Add<IndirectBadHost>(Lifetime.Host).Build();
        OwnedScope scene = host.CreateScope<World>().CreateScope<Scene>();

        // Act
        Func<Task> resolve = () => scene.ResolveAsync<IndirectBadHost>().AsTask();

        // Assert
        await Check.FailsAsync<DependencyInjectionException>(resolve, "resolving from Host");
    }

    [Test]
    public void BuildValidatesWithoutRunningConstructorsOrFactories() {
        // Arrange
        int calls = 0;
        Action buildMissingDependency = () => Services()
            .AddFactory<HostService>(Lifetime.Host, factory: _ => {
                calls++;
                return new HostService();
            })
            .AddModule("broken-module", configure: c => c.Add<MissingConsumer>(Lifetime.Host)).Build();
        Action buildCycle = () => Services().Add<CycleA>(Lifetime.Host).Add<CycleB>(Lifetime.Host).Build();
        Action buildAmbiguous = () => Services().Add<Ambiguous>(Lifetime.Host).Build();
        Action buildWrongImplementation = () => new ServiceCollection()
            .Add(new ServiceRecord(Lifetime.Host, typeof(HostService), typeof(WorldService))).Build();
        Action buildAbstract = () => new ServiceCollection().Add<AbstractService>(Lifetime.Host).Build();

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
    public async Task DuplicatesAreRejectedAndBuildFreezesConfiguration() {
        // Arrange
        ServiceCollection collection = Services().AddModule("first", configure: c => c.Add<HostService>(Lifetime.Host));

        // Act
        Check.Fails<DependencyInjectionException>(action: () => collection.AddModule("second", configure: c => c.Add<HostService>(Lifetime.Transient)), "first");
        await using ServiceProvider host = collection.Build();

        // Assert
        Check.Fails<InvalidOperationException>(action: () => collection.Add<Helper>(Lifetime.Transient), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.DeclareScope<CustomScope>(typeof(Host)), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.RequireInput<World, WorldConfig>(), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.Build(), "immutable");
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<Helper>().AsTask(), "Unregistered");
    }

    [Test]
    public async Task ExtensibleScopesValidateParentRelationships() {
        // Arrange
        await using ServiceProvider host = Services().DeclareScope<CustomScope>(typeof(World))
            .Add<Helper>(Lifetime.Of<CustomScope>()).Build();
        OwnedScope world = host.CreateScope<World>();
        OwnedScope custom = world.CreateScope<CustomScope>();

        // Act
        Helper helper = await custom.ResolveAsync<Helper>();

        // Assert
        Check.Same(helper, await custom.ResolveAsync<Helper>());
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<Scene>(), "cannot be created");
        Check.Fails<DependencyInjectionException>(action: () => world.CreateScope<World>(), "cannot be created");
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<CustomScope>(), "cannot be created");
        Check.Fails<DependencyInjectionException>(action: () => custom.CreateScope<UnknownScope>(), "cannot be created");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().DeclareScope<CustomScope>(typeof(UnknownScope)).Build(), "Undeclared");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().DeclareScope<CustomScope>(typeof(CustomScope)).Build(), "cycle");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().Add<Helper>(Lifetime.Of<UnknownScope>()).Build(), "Undeclared");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().DeclareScope<CustomScope>().Build(), "path to Host");
        Check.Fails<DependencyInjectionException>(action: () => Services().DeclareScope<CustomScope>(typeof(Host), typeof(World))
            .Add<WorldService>(Lifetime.Of<World>()).Add<WorldHelper>(Lifetime.Of<CustomScope>()).Build(), "Lifetime violation");
    }

    [Test]
    public void InputsCannotOverrideServices() {
        // Arrange
        var serviceFirst = new ServiceCollection();
        var inputFirst = new ServiceCollection();

        // Act
        Action addInput = () => serviceFirst.Add<WorldService>(Lifetime.Host).RequireInput<World, WorldService>();
        Action addService = () => inputFirst.RequireInput<World, WorldService>().Add<WorldService>(Lifetime.Host);

        // Assert
        Check.Fails<DependencyInjectionException>(addInput, "conflicts");
        Check.Fails<DependencyInjectionException>(addService, "Conflicts");
    }

    [Test]
    public async Task LegacyServiceRecordMapsToTypedLifetimes() {
        // Arrange
        await using ServiceProvider host = Services()
            .Add(new ServiceRecord(ServiceScope.Singleton, typeof(HostService), typeof(HostService))).Build();

        // Act
        HostService service = await host.ResolveAsync<HostService>();
        HostService serviceFromWorld = await host.CreateScope<World>().ResolveAsync<HostService>();

        // Assert
        Check.Same(service, serviceFromWorld);
    }

    private static ServiceCollection Services() {
        var services = new ServiceCollection();
        TestActivators.AddActivators(services);
        return services;
    }

    public sealed class HostService;

    public sealed class WorldService;

    public sealed class SceneService;

    public sealed class Helper;

    public sealed record WorldConfig(int Seed);

    public sealed record SceneConfig(string Name);

    public sealed class ConfiguredWorld(WorldConfig config) {
        public WorldConfig Config { get; } = config;
    }

    public sealed class BadHost(WorldService world) {
        public WorldService World { get; } = world;
    }

    public sealed class WorldHelper(WorldService world) {
        public WorldService World { get; } = world;
    }

    public sealed class IndirectBadHost(WorldHelper helper) {
        public WorldHelper Helper { get; } = helper;
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
        public Ambiguous(Helper helper) {}
    }

    public abstract class AbstractService;

    public sealed class CustomScope;

    public sealed class UnknownScope;
}

[GenerateServiceActivators(typeof(ContainerTests.HostService), typeof(ContainerTests.WorldService),
    typeof(ContainerTests.SceneService), typeof(ContainerTests.Helper), typeof(ContainerTests.ConfiguredWorld),
    typeof(ContainerTests.BadHost), typeof(ContainerTests.WorldHelper), typeof(ContainerTests.IndirectBadHost),
    typeof(ContainerTests.BadInputWorld), typeof(ContainerTests.MissingConsumer), typeof(ContainerTests.CycleA), typeof(ContainerTests.CycleB))]
internal static partial class TestActivators;

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
