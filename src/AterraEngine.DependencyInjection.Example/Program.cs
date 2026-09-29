using System.Runtime.CompilerServices;
using AterraEngine.Core.DependencyInjection;

namespace AterraEngine.DependencyInjection.Example;
internal static class Program {
    private static async Task Main(string[] args) {
        if (args.Contains("--require-native-aot"))
            Require(!RuntimeFeature.IsDynamicCodeSupported, "This smoke check must run as Native AOT.");
        var services = new ServiceCollection();
        services.AddModule("headless-game", GameModule.Configure);
        ServiceProvider host = services.Build();
        await using ServiceProvider cleanup = host;
        OwnedServiceScope earth = host.CreateScope<AterraWorld>(ServiceScopeInput.Of(new WorldConfig("Earth", 42)));
        OwnedServiceScope mars = host.CreateScope<AterraWorld>(ServiceScopeInput.Of(new WorldConfig("Mars", 73)));
        OwnedServiceScope town = earth.CreateScope<AterraScene>();
        OwnedServiceScope dungeon = earth.CreateScope<AterraScene>();
        OwnedServiceScope colony = mars.CreateScope<AterraScene>();

        var first = await town.ResolveAsync<SceneSession>();
        var second = await dungeon.ResolveAsync<SceneSession>();
        var third = await colony.ResolveAsync<SceneSession>();
        Require(ReferenceEquals(first.World, second.World), "Sibling scenes must share their world.");
        Require(!ReferenceEquals(first.World, third.World), "Worlds must be independent.");
        Require(ReferenceEquals(first.World.Log, third.World.Log), "Both worlds must share the host log.");
        Require(!ReferenceEquals(first, second), "Scenes must be distinct.");
        first.World.Tick();
        Require(second.World.Ticks == 1 && third.World.Ticks == 0, "World state must be independent.");
        await earth.DisposeAsync();
        Require(first.IsDisposed && second.IsDisposed && first.World.IsDisposed, "Earth teardown must clean its subtree.");
        Require(!third.IsDisposed && !third.World.IsDisposed, "Mars must remain alive.");
        third.World.Tick();
        await host.DisposeAsync();
        Require(third.IsDisposed && third.World.IsDisposed && third.World.Log.IsDisposed, "Host teardown must finish cleanup.");
        Console.WriteLine("PASS: generated activation, sharing, independent world state, and cleanup.");
    }

    private static void Require(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class GameModule {
    internal static void Configure(ServiceCollection services) {
        services.RegisterActivators<WorldConfig>()
            .RequireInput<AterraWorld, WorldConfig>();
    }
}

internal sealed record WorldConfig(string Name, int Seed);

[HostService<EngineLog>]
internal sealed class EngineLog : IDisposable {
    public bool IsDisposed { get; private set; }
    public void Dispose() {
        IsDisposed = true;
        Write("Disposed host log");
    }
    public void Write(string message) => Console.WriteLine(message);
}

[WorldService<WorldSimulation>]
internal sealed class WorldSimulation(EngineLog log, WorldConfig config) : IAsyncDisposable {
    public EngineLog Log { get; } = log;
    public WorldConfig Config { get; } = config;
    public int Ticks { get; private set; }
    public bool IsDisposed { get; private set; }
    public ValueTask DisposeAsync() {
        IsDisposed = true;
        Log.Write($"Disposed world {Config.Name}");
        return ValueTask.CompletedTask;
    }
    public void Tick() {
        Ticks++;
        Log.Write($"{Config.Name} (seed {Config.Seed}): tick {Ticks}");
    }
}

[SceneService<SceneSession>]
internal sealed class SceneSession(WorldSimulation world) : IDisposable {
    public WorldSimulation World { get; } = world;
    public bool IsDisposed { get; private set; }
    public void Dispose() {
        IsDisposed = true;
        World.Log.Write($"Disposed scene in {World.Config.Name}");
    }
}
