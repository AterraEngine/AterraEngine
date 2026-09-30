using AterraEngine.Core.DependencyInjection.Tests.Fixtures;

namespace AterraEngine.Core.DependencyInjection.Tests.Collection;
public sealed class MultipleRegistrationTests {
    [Test]
    public async Task AppendResolvesInRegistrationOrderAndDirectResolutionUsesLast() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddEnumerableFactory<TestFixtures.IPlugin>(ServiceLifetime.Host, factory: _ => new TestFixtures.PluginA())
            .AddEnumerableFactory<TestFixtures.IPlugin>(ServiceLifetime.Host, factory: _ => new TestFixtures.PluginB())
            .Build();

        TestFixtures.IPlugin[] values = (await provider.ResolveAsync<IEnumerable<TestFixtures.IPlugin>>()).ToArray();
        await Assert.That(values.Count()).IsEqualTo(2);
        await Assert.That(values[0].Name).IsEqualTo("a");
        await Assert.That(values[1].Name).IsEqualTo("b");
        await Assert.That((await provider.ResolveAsync<TestFixtures.IPlugin>()).Name).IsEqualTo("b");
    }

    [Test]
    public async Task ReplacementClearsPreviousAppendedRegistrations() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddEnumerableFactory<TestFixtures.IPlugin>(ServiceLifetime.Host, factory: _ => new TestFixtures.PluginA())
            .AddFactory<TestFixtures.IPlugin>(ServiceLifetime.Host, factory: _ => new TestFixtures.PluginB())
            .Build();

        TestFixtures.IPlugin[] values = (await provider.ResolveAsync<IEnumerable<TestFixtures.IPlugin>>()).ToArray();
        await Assert.That(values).HasSingleItem();
        await Assert.That(values[0].Name).IsEqualTo("b");
    }

    [Test]
    public async Task GeneratedConstructorCanReceiveAnEmptyCollection() {
        await using ServiceProvider provider = new ServiceCollection().RegisterActivators<TestFixtures.EmptyCollectionConsumer>().Build();

        var consumer = await provider.ResolveAsync<TestFixtures.EmptyCollectionConsumer>();
        await Assert.That(consumer.Values).IsEmpty();
    }

    [Test]
    public async Task GeneratedRegistrationsResolveAllImplementationsInStableOrder() {
        await using ServiceProvider provider = new ServiceCollection()
            .RegisterActivators<TestFixtures.PluginA>()
            .Build();

        TestFixtures.IPlugin[] values = (await provider.ResolveAsync<IEnumerable<TestFixtures.IPlugin>>()).ToArray();
        await Assert.That(values.Count()).IsEqualTo(2);
        await Assert.That(values[0].Name).IsEqualTo("a");
        await Assert.That(values[1].Name).IsEqualTo("b");
    }
}
