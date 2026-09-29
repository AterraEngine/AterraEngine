# Dependency injection

The container is headless and Native AOT compatible. Modules contribute explicit registrations; a host and its typed child scopes own instances. Authored entities, quests, assets and other content are **not** individual service registrations.

## Generated activation

Reference `AterraEngine.Core.DependencyInjection`, plus the generator as a build-time analyzer:

```xml
<ProjectReference Include="../AterraEngine.Core.DependencyInjection.Generators/AterraEngine.Core.DependencyInjection.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Each module explicitly lists its implementation types. The generator emits `AddActivators(ServiceCollection)` with direct `new` calls and dependency type arrays. It does not discover assemblies, scan runtime types, use `Activator`, compile expressions, or invoke constructors through reflection. Runtime `Type` values are identity/assignability keys, which are supported by Native AOT.

```csharp
using AterraEngine.Core.DependencyInjection;
using AterraEngine.Core.DependencyInjection.Collection;
using AterraEngine.Core.DependencyInjection.Scopes;

[GenerateServiceActivators(typeof(Clock), typeof(Simulation))]
public static partial class GameModule {
    public static void Configure(ServiceCollection services) {
        AddActivators(services);
        services.RequireInput<World, WorldConfig>()
            .Add<Clock>(Lifetime.Host)
            .Add<Simulation>(Lifetime.Of<World>());
    }
}

public sealed record WorldConfig(int Seed);
public sealed class Clock;
public sealed class Simulation(Clock clock, WorldConfig config) {
    public Clock Clock { get; } = clock;
    public WorldConfig Config { get; } = config;
}

// In the application's entry point:
// var services = new ServiceCollection().AddModule("game", GameModule.Configure);
// await using ServiceProvider host = services.Build();
// OwnedScope world = host.CreateScope<World>(ScopeInput.Of(new WorldConfig(42)));
// OwnedScope scene = world.CreateScope<Scene>();
// Simulation simulation = await scene.ResolveAsync<Simulation>();
```

An activator recipe is not a registration. Listing an implementation in the attribute does not make it resolvable. Register each service with `Add<T>(lifetime)` or `Add<TService, TImplementation>(lifetime)`. Install a recipe once per collection; separate modules can register distinct services using the same installed implementation recipe.

**Constructor rule:** exactly one public instance constructor. All parameters, including optional parameters, must be registered services or declared inputs. There is no “greediest constructor,” optional-argument fallback, or implicit concrete construction. The analyzer reports `ADI001` for invalid declarations. Modules must be top-level, non-generic static partial classes. Implementations must be closed, accessible concrete classes. Ref/out/in, pointer, dynamic and ref-like parameters are unsupported. Required members require a constructor marked `SetsRequiredMembers`.

The incremental generator follows the [Roslyn cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md): `ForAttributeWithMetadataName`, value-equatable string models, text emission, no retained compiler symbols, and a separate diagnostic analyzer. Roslyn is a build-time dependency only.

## Configuration and validation

`ServiceCollection` is a single-threaded builder. A successful `Build` freezes it and creates **one host**. Use a fresh collection and rerun module contributions for another host. A failed build leaves configuration editable and does not transfer ownership of external objects.

Duplicate single-service registrations are errors, including registrations from different modules. Diagnostics retain module names and dependency paths. The previous scaffold did not implement collection registrations; this API does not synthesize `IEnumerable<T>`.

Build validates the generated dependency graph without running user constructors or factories:

- missing registrations and dependency cycles;
- incompatible implementations and undeclared lifetime scopes;
- direct and transitive lifetime violations, including through transients;
- scope parent topology and input declarations.

`AddFactory<T>(lifetime, resolver => ...)` is intentionally opaque. Build cannot validate its hidden dependencies; each `resolver.Get<T>()` performs registration, ancestry and cycle checks at runtime. Factory resolvers are synchronous, thread-confined, and expire when the invocation returns. Factories must use the supplied resolver, rather than starting another public resolution or scheduling dependency resolution on another thread. Reentrant public resolution on an activating thread is rejected before it can deadlock. A factory must return a new instance; returning an already-owned or caller-owned disposable alias is rejected.

`AddGeneratedActivator<T>(factory, dependencyTypes)` is the stack-only target of generated code. `AddActivator<T>(factory, dependencyTypes)` supports handwritten recipes, where the author is responsible for accurate dependency metadata. There is no reflective fallback for a missing recipe.

`ServiceProvider` and `System.IServiceProvider` are implicit Host services. Constructor activators and factories can request either type, and both resolve to the current host provider without an explicit registration. These service types are reserved and cannot be overridden or declared as scope inputs. `IServiceProvider.GetService` returns `null` for an unknown service; activation and scope errors from known services still propagate.

## Ownership scopes and lifetimes

The built-in parent graph is `Host → World → Scene`. Extend it explicitly:

```csharp
services.DeclareScope<Session>(typeof(World));
services.Add<SessionService>(Lifetime.Of<Session>());
// Install the generated SessionService activator as well.
```

Scope declarations form an acyclic graph rooted at Host. A scope can have multiple allowed parent types; build-time lifetime validation requires the dependency owner to be available on **every** allowed parent path. Repeating a scope type in an ancestry chain is prohibited.

| Lifetime | Owner |
| --- | --- |
| `Lifetime.Host` | The engine host |
| `Lifetime.Of<TScope>()` | Nearest enclosing scope of that type |
| `Lifetime.Transient` | Fresh instance per resolution; disposable ownership follows the current activation anchor |

The original `ServiceScope` enum remains as a `ServiceRecord` compatibility adapter; `Singleton` means Host. Typed lifetimes are the extensible API.

Cached services are **constructed from their owner**, not the descendant that requested them. A World service first requested from a Scene can see Host/World services and inputs, but not Scene services or inputs. Its transient helpers also resolve from World. A Host → transient → World chain is invalid. Missing owners fail immediately; the container never creates scopes implicitly or searches siblings/descendants. There is no ambient current world or global mutable registry.

## Scope inputs

`RequireInput<TScope, TInput>()` declares a required exact-type binding. Supply it using `ScopeInput.Of<TInput>(value)` at scope creation (or `Build` for Host inputs). All required inputs must be present before the scope is published. Unknown and duplicate inputs are rejected.

Each input type has one declared owner type and cannot also be a service. Bindings cannot change after scope creation. Use immutable values, such as records; DI does not deep-copy objects or enforce deep immutability of user types. Inputs are caller-owned and are never disposed by the container. Inputs remain visible in descendants, subject to owner-anchored activation. Use distinct wrapper types when multiple values have the same primitive representation, such as world seed and difficulty.

## Cleanup and failures

Use `await using` / `DisposeAsync`. There is intentionally no synchronous scope-disposal API or sync-over-async fallback. Public `ResolveAsync<T>` can await rollback of async-only disposable resources; constructors and factory delegates themselves execute synchronously on the calling thread.

- Child scopes finish cleanup before parent services; siblings are visited in reverse creation order.
- Within an owner, disposable instances are released in reverse successful-construction order. Concurrent completions are ordered when ownership is claimed. Container-owned external instances are adopted at Build in registration order.
- `IAsyncDisposable` takes precedence over `IDisposable` when an object implements both. Cleanup happens exactly once.
- `AddInstance(value, InstanceOwnership.Caller)` leaves cleanup to the caller; `Container` transfers ownership after successful Build, even if the service is never resolved. External instances are Host registrations. The same external object cannot be registered twice or reused as a scope input.
- A failed activation rolls back its unpublished disposable transients. Successfully cached dependencies and their transients remain owned by their scopes. Constructors must clean resources they allocate privately before throwing; DI can only track successfully returned instances.
- Cleanup continues after individual failures and reports `AggregateException`. If rollback also fails, both activation and cleanup errors are reported.
- Repeated/concurrent disposal awaits the same task, including the same reported cleanup failure. Disposal clears caches, ownership tracking, inputs, and child links; independently disposed worlds are removed from their host.

Factories that catch a nested activation error can continue, but resources from the failed branch are still rolled back before the public resolution completes.

## Concurrency and shutdown

Per-registration/per-owner cache slots allow at most one construction. Concurrent callers share the value or the cached activation exception. A failed cached slot stays faulted for that scope's lifetime; create a new scope to retry. Transient failures do not poison other resolutions.

Short provider-local critical sections protect cache publication, the activation wait graph, ownership, and scope-tree state. User constructors and disposal callbacks run outside the provider gate. Runtime cycle checks include concurrent opaque factories, so cross-thread dependency cycles fail rather than deadlock.

Shutdown atomically stops the subtree, rejecting new resolution and child creation. Admitted resolutions may finish and publish to their existing owners; shutdown awaits their construction and rollback before cleanup. A racing child is either rejected or included in its parent's teardown. Independent worlds can shut down while work in another world continues.

**Callers must stop and join jobs using resolved services before disposing their scopes.** DI coordinates activation, not arbitrary consumer activity. A racing admitted resolution can return an instance immediately before shutdown disposes it. Do not start or await scope shutdown from its own constructor, factory, or cleanup callback.

## Verification and headless example

The example creates one host, two worlds and three scenes, verifies sharing/state isolation, disposes one world independently, and then shuts down the host. Its project enables Native AOT and treats AOT warnings as errors.

```powershell
dotnet build AtteraEngine.slnx -c Release
dotnet test --solution AtteraEngine.slnx -c Release
dotnet publish src/AterraEngine.DependencyInjection.Example/AterraEngine.DependencyInjection.Example.csproj -c Release -r win-x64
& ./src/AterraEngine.DependencyInjection.Example/bin/Release/net11.0/win-x64/publish/AterraEngine.DependencyInjection.Example.exe --require-native-aot
```

The SDK is pinned in `global.json`, which also selects Microsoft Testing Platform for TUnit. Native publishing requires the platform's native compiler toolchain. The DI library and headless example are verified independently of rendering, editor UI, plugin loading, or any future engine subsystem.
