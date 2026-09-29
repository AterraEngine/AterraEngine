using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

namespace AterraEngine.Core.DependencyInjection.Tests;
public class ContainerTests {
    [Test]
    public async Task HostIsolationAndTransientIdentity() {
        static ServiceCollection Configure() {
            return Services().Add<HostService>(Lifetime.Host).Add<Helper>(Lifetime.Transient);
        }

        await using ServiceProvider first = Configure().Build();
        await using ServiceProvider second = Configure().Build();
        var service = await first.ResolveAsync<HostService>();
        Check.Same(service, await first.ResolveAsync<HostService>());
        Check.Different(service, await second.ResolveAsync<HostService>());
        Check.Different(await first.ResolveAsync<Helper>(), await first.ResolveAsync<Helper>());
    }

    [Test]
    public async Task WorldsShareAcrossSiblingScenesButRemainIndependent() {
        await using ServiceProvider host = Services()
            .Add<WorldService>(Lifetime.Of<World>()).Add<SceneService>(Lifetime.Of<Scene>()).Build();
        OwnedScope worldA = host.CreateScope<World>();
        OwnedScope worldB = host.CreateScope<World>();
        OwnedScope sceneA = worldA.CreateScope<Scene>();
        OwnedScope sceneB = worldA.CreateScope<Scene>();
        OwnedScope sceneC = worldB.CreateScope<Scene>();
        Check.Same(await sceneA.ResolveAsync<WorldService>(), await sceneB.ResolveAsync<WorldService>());
        Check.Different(await sceneA.ResolveAsync<WorldService>(), await sceneC.ResolveAsync<WorldService>());
        Check.Same(await sceneA.ResolveAsync<SceneService>(), await sceneA.ResolveAsync<SceneService>());
        Check.Different(await sceneA.ResolveAsync<SceneService>(), await sceneB.ResolveAsync<SceneService>());
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<WorldService>().AsTask(), "Missing ownership scope");
        await Check.FailsAsync<DependencyInjectionException>(action: () => worldA.ResolveAsync<SceneService>().AsTask(), "Missing ownership scope");
    }

    [Test]
    public async Task InputsAreTypedRequiredIndependentAndAnchored() {
        ServiceCollection collection = Services().RequireInput<World, WorldConfig>()
            .RequireInput<Scene, SceneConfig>().Add<ConfiguredWorld>(Lifetime.Of<World>());
        await using ServiceProvider host = collection.Build();
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<World>(), "Required input");
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<World>(ScopeInput.Of(new SceneConfig("wrong"))), "not declared");
        Check.Fails<DependencyInjectionException>(action: () => host.CreateScope<World>(ScopeInput.Of(new WorldConfig(1)), ScopeInput.Of(new WorldConfig(2))), "Duplicate input");
        OwnedScope a = host.CreateScope<World>(ScopeInput.Of(new WorldConfig(10)));
        OwnedScope b = host.CreateScope<World>(ScopeInput.Of(new WorldConfig(20)));
        OwnedScope scene = a.CreateScope<Scene>(ScopeInput.Of(new SceneConfig("scene")));
        await Assert.That((await scene.ResolveAsync<ConfiguredWorld>()).Config.Seed).IsEqualTo(10);
        await Assert.That((await b.ResolveAsync<ConfiguredWorld>()).Config.Seed).IsEqualTo(20);
        Check.Same(await scene.ResolveAsync<ConfiguredWorld>(), await a.ResolveAsync<ConfiguredWorld>());
        await Check.FailsAsync<DependencyInjectionException>(action: () => a.ResolveAsync<SceneConfig>().AsTask(), "Missing ownership scope");
    }

    [Test]
    public async Task OpaqueFactoryCannotCaptureRequestingSceneOrItsInput() {
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
        await Check.FailsAsync<DependencyInjectionException>(action: () => scene.ResolveAsync<WorldService>().AsTask(), "resolving from World");
        await Check.FailsAsync<DependencyInjectionException>(action: () => scene.ResolveAsync<ConfiguredWorld>().AsTask(), "resolving from World");
    }

    [Test]
    public void BuildRejectsDirectAndTransitiveLifetimeViolations() {
        Check.Fails<DependencyInjectionException>(action: () => Services()
            .Add<WorldService>(Lifetime.Of<World>()).Add<BadHost>(Lifetime.Host).Build(), "Lifetime violation");
        Check.Fails<DependencyInjectionException>(action: () => Services()
            .Add<WorldService>(Lifetime.Of<World>()).Add<WorldHelper>(Lifetime.Transient)
            .Add<IndirectBadHost>(Lifetime.Host).Build(), "Lifetime violation");
        Check.Fails<DependencyInjectionException>(action: () => Services()
            .RequireInput<Scene, SceneConfig>().Add<BadInputWorld>(Lifetime.Of<World>()).Build(), "Lifetime violation");
    }

    [Test]
    public async Task RuntimeLifetimeCheckIncludesTransientFactoryDependencies() {
        await using ServiceProvider host = Services().Add<WorldService>(Lifetime.Of<World>())
            .AddFactory<WorldHelper>(Lifetime.Transient, factory: r => new WorldHelper(r.Get<WorldService>()))
            .Add<IndirectBadHost>(Lifetime.Host).Build();
        OwnedScope scene = host.CreateScope<World>().CreateScope<Scene>();
        await Check.FailsAsync<DependencyInjectionException>(action: () => scene.ResolveAsync<IndirectBadHost>().AsTask(), "resolving from Host");
    }

    [Test]
    public void BuildValidatesWithoutRunningConstructorsOrFactories() {
        int calls = 0;
        Check.Fails<DependencyInjectionException>(action: () => Services()
            .AddFactory<HostService>(Lifetime.Host, factory: _ => {
                calls++;
                return new HostService();
            })
            .AddModule("broken-module", configure: c => c.Add<MissingConsumer>(Lifetime.Host)).Build(), "broken-module");
        Check.True(calls == 0, "Build invoked a factory.");
        Check.Fails<DependencyInjectionException>(action: () => Services().Add<CycleA>(Lifetime.Host).Add<CycleB>(Lifetime.Host).Build(), "cycle");
        Check.Fails<DependencyInjectionException>(action: () => Services().Add<Ambiguous>(Lifetime.Host).Build(), "No generated activator");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection()
            .Add(new ServiceRecord(Lifetime.Host, typeof(HostService), typeof(WorldService))).Build(), "Invalid implementation");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().Add<AbstractService>(Lifetime.Host).Build(), "Invalid implementation");
    }

    [Test]
    public async Task DuplicatesAreRejectedAndBuildFreezesConfiguration() {
        ServiceCollection collection = Services().AddModule("first", configure: c => c.Add<HostService>(Lifetime.Host));
        Check.Fails<DependencyInjectionException>(action: () => collection.AddModule("second", configure: c => c.Add<HostService>(Lifetime.Transient)), "first");
        await using ServiceProvider host = collection.Build();
        Check.Fails<InvalidOperationException>(action: () => collection.Add<Helper>(Lifetime.Transient), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.DeclareScope<CustomScope>(typeof(Host)), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.RequireInput<World, WorldConfig>(), "immutable");
        Check.Fails<InvalidOperationException>(action: () => collection.Build(), "immutable");
        await Check.FailsAsync<DependencyInjectionException>(action: () => host.ResolveAsync<Helper>().AsTask(), "Unregistered");
    }

    [Test]
    public async Task ExtensibleScopesValidateParentRelationships() {
        await using ServiceProvider host = Services().DeclareScope<CustomScope>(typeof(World))
            .Add<Helper>(Lifetime.Of<CustomScope>()).Build();
        OwnedScope world = host.CreateScope<World>();
        OwnedScope custom = world.CreateScope<CustomScope>();
        Check.Same(await custom.ResolveAsync<Helper>(), await custom.ResolveAsync<Helper>());
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
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().Add<WorldService>(Lifetime.Host).RequireInput<World, WorldService>(), "conflicts");
        Check.Fails<DependencyInjectionException>(action: () => new ServiceCollection().RequireInput<World, WorldService>().Add<WorldService>(Lifetime.Host), "Conflicts");
    }

    [Test]
    public async Task LegacyServiceRecordMapsToTypedLifetimes() {
        await using ServiceProvider host = Services()
            .Add(new ServiceRecord(ServiceScope.Singleton, typeof(HostService), typeof(HostService))).Build();
        Check.Same(await host.ResolveAsync<HostService>(), await host.CreateScope<World>().ResolveAsync<HostService>());
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
