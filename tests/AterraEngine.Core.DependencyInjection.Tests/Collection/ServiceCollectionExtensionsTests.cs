// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection.Tests.Collection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class ServiceCollectionExtensionsTests {
    [Test]
    public async Task TypedAndRecordRegistrationOverloadsRegisterServices() {
        await using ServiceProvider typed = new ServiceCollection()
            .AddActivator<Thing>(_ => new Thing("typed"))
            .Add<Thing>(ServiceLifetime.Host)
            .Build();
        await using ServiceProvider mapped = new ServiceCollection()
            .AddActivator<Thing>(_ => new Thing("mapped"))
            .Add<IThing, Thing>(ServiceLifetime.Host)
            .Build();
        await using ServiceProvider records = new ServiceCollection()
            .AddActivator<Thing>(_ => new Thing("record"))
            .Add(new ServiceRecord(ServiceLifetime.Host, typeof(IThing), typeof(Thing)))
            .AddEnumerable<IThing, Thing>(ServiceLifetime.Host)
            .AddEnumerable(new ServiceRecord(ServiceLifetime.Host, typeof(IThing), typeof(Thing)))
            .Build();

        await Assert.That((await typed.ResolveAsync<Thing>()).Name).IsEqualTo("typed");
        await Assert.That((await mapped.ResolveAsync<IThing>()).Name).IsEqualTo("mapped");
        await Assert.That((await records.ResolveAsync<IEnumerable<IThing>>()).Count()).IsEqualTo(3);
    }

    [Test]
    public async Task FactoryAndInstanceOverloadsRegisterSingleAndEnumerableServices() {
        var first = new Thing("instance");
        var second = new Thing("enumerable-instance");
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<IThing>(ServiceLifetime.Host, factory: _ => new Thing("factory"))
            .AddEnumerableFactory<IThing>(ServiceLifetime.Host, factory: _ => new Thing("enumerable-factory"))
            .AddInstance(first, ServiceInstanceOwnership.Caller)
            .AddEnumerableInstance(second, ServiceInstanceOwnership.Caller)
            .Build();

        var resolved = await provider.ResolveAsync<IThing>();
        IThing[] all = (await provider.ResolveAsync<IEnumerable<IThing>>()).ToArray();
        Thing[] instances = (await provider.ResolveAsync<IEnumerable<Thing>>()).ToArray();

        await Assert.That(resolved.Name).IsEqualTo("enumerable-factory");
        await Assert.That(all.Select(value => value.Name)).IsEquivalentTo(["factory", "enumerable-factory"]);
        await Assert.That(instances.Select(value => value.Name)).IsEquivalentTo(["instance", "enumerable-instance"]);
        await Assert.That(instances).Contains(first);
        await Assert.That(instances).Contains(second);
    }

    [Test]
    public async Task KeyedImplementationOverloadsRegisterTypedRuntimeAndNamedKeys() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddActivator<Thing>(_ => new Thing("implementation"))
            .AddKeyed<IThing, Thing, string>(ServiceLifetime.Host, "typed")
            .AddKeyed<IThing, Thing, string>("typed-reverse", ServiceLifetime.Host)
            .AddKeyed<IThing, Thing>(ServiceLifetime.Host, "runtime")
            .AddKeyed<IThing, Thing>("runtime-reverse", ServiceLifetime.Host)
            .AddNamed<IThing, Thing>("named", ServiceLifetime.Host)
            .AddKeyedEnumerable<IThing, Thing, string>(ServiceLifetime.Host, "enumerable")
            .AddKeyedEnumerable<IThing, Thing>(ServiceLifetime.Host, "enumerable-runtime")
            .AddNamedEnumerable<IThing, Thing>("enumerable-named", ServiceLifetime.Host)
            .Build();

        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("typed")).Name).IsEqualTo("implementation");
        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("typed-reverse")).Name).IsEqualTo("implementation");
        await Assert.That((await provider.ResolveKeyedAsync<IThing>("runtime")).Name).IsEqualTo("implementation");
        await Assert.That((await provider.ResolveKeyedAsync<IThing>("runtime-reverse")).Name).IsEqualTo("implementation");
        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("named")).Name).IsEqualTo("implementation");
        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("enumerable")).Name).IsEqualTo("implementation");
        await Assert.That((await provider.ResolveKeyedAsync<IThing>("enumerable-runtime")).Name).IsEqualTo("implementation");
        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("enumerable-named")).Name).IsEqualTo("implementation");
    }

    [Test]
    public async Task KeyedFactoryAndInstanceOverloadsRegisterAllForms() {
        var instance = new Thing("instance");
        var enumerableInstance = new Thing("enumerable-instance");
        await using ServiceProvider provider = new ServiceCollection()
            .AddKeyedFactory<IThing, string>(ServiceLifetime.Host, "factory", factory: _ => new Thing("factory"))
            .AddKeyedFactory<IThing, string>("factory-reverse", ServiceLifetime.Host, factory: _ => new Thing("factory-reverse"))
            .AddNamedFactory<IThing>("named-factory", ServiceLifetime.Host, factory: _ => new Thing("named-factory"))
            .AddKeyedEnumerableFactory<IThing, string>(ServiceLifetime.Host, "enumerable-factory", factory: _ => new Thing("enumerable-factory"))
            .AddKeyedInstance("instance", instance, ServiceInstanceOwnership.Caller)
            .AddKeyedEnumerableInstance("enumerable-instance", enumerableInstance, ServiceInstanceOwnership.Caller)
            .AddNamedInstance("named-instance", new Thing("named-instance"), ServiceInstanceOwnership.Caller)
            .Build();

        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("factory")).Name).IsEqualTo("factory");
        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("factory-reverse")).Name).IsEqualTo("factory-reverse");
        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("named-factory")).Name).IsEqualTo("named-factory");
        await Assert.That((await provider.ResolveKeyedAsync<IThing, string>("enumerable-factory")).Name).IsEqualTo("enumerable-factory");
        await Assert.That(await provider.ResolveKeyedAsync<Thing, string>("instance")).IsSameReferenceAs(instance);
        await Assert.That(await provider.ResolveKeyedAsync<Thing, string>("enumerable-instance")).IsSameReferenceAs(enumerableInstance);
        await Assert.That((await provider.ResolveKeyedAsync<Thing, string>("named-instance")).Name).IsEqualTo("named-instance");
    }

    [Test]
    public async Task DecorationOverloadsWrapUnkeyedAndKeyedServices() {
        await using ServiceProvider factory = new ServiceCollection()
            .AddFactory<IThing>(ServiceLifetime.Host, factory: _ => new Thing("factory"))
            .Decorate<IThing, ThingDecorator>(inner => new ThingDecorator(inner, "factory-decorator"))
            .Build();
        await using ServiceProvider generated = new ServiceCollection()
            .AddFactory<IThing>(ServiceLifetime.Host, factory: _ => new Thing("generated"))
            .AddGeneratedActivator<ThingDecorator>(static (ref resolver) => new ThingDecorator(resolver.GetInner<IThing>(), "generated-decorator"))
            .Decorate<IThing, ThingDecorator>()
            .Build();
        await using ServiceProvider keyedFactory = new ServiceCollection()
            .AddKeyedFactory<IThing, string>(ServiceLifetime.Host, "key", factory: _ => new Thing("keyed"))
            .Decorate<IThing, ThingDecorator, string>("key", decorator: inner => new ThingDecorator(inner, "keyed-decorator"))
            .Build();
        await using ServiceProvider keyedGenerated = new ServiceCollection()
            .AddKeyedFactory<IThing, string>(ServiceLifetime.Host, "key", factory: _ => new Thing("keyed-generated"))
            .AddGeneratedActivator<ThingDecorator>(static (ref resolver) => new ThingDecorator(resolver.GetInner<IThing>(), "keyed-generated-decorator"))
            .Decorate<IThing, ThingDecorator, string>("key")
            .Build();
        await using ServiceProvider explicitGenerated = new ServiceCollection()
            .AddFactory<IThing>(ServiceLifetime.Host, factory: _ => new Thing("explicit"))
            .Decorate<IThing, ThingDecorator>(static (ref resolver) => new ThingDecorator(resolver.GetInner<IThing>(), "explicit-decorator"))
            .Build();

        await Assert.That((await factory.ResolveAsync<IThing>()).Name).IsEqualTo("factory-decorator:factory");
        await Assert.That((await generated.ResolveAsync<IThing>()).Name).IsEqualTo("generated-decorator:generated");
        await Assert.That((await keyedFactory.ResolveKeyedAsync<IThing, string>("key")).Name).IsEqualTo("keyed-decorator:keyed");
        await Assert.That((await keyedGenerated.ResolveKeyedAsync<IThing, string>("key")).Name).IsEqualTo("keyed-generated-decorator:keyed-generated");
        await Assert.That((await explicitGenerated.ResolveAsync<IThing>()).Name).IsEqualTo("explicit-decorator:explicit");
    }

    public interface IThing {
        string Name { get; }
    }

    private sealed class Thing(string name) : IThing {
        public string Name { get; } = name;
    }

    private sealed class ThingDecorator(IThing inner, string prefix) : IThing {
        public string Name => $"{prefix}:{inner.Name}";
    }
}
