using JetBrains.Annotations;

namespace AterraEngine.Core.DependencyInjection.Tests.Configuration;
public sealed class KeyedServiceTests {
    [Test]
    public async Task KeysReplaceOnlyTheSameCompositeIdentityAndDoNotEnterUnkeyedCollections() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddKeyedEnumerableFactory<IValue, string>(ServiceLifetime.Host, "a", factory: _ => new Value("a1"))
            .AddKeyedEnumerableFactory<IValue, string>(ServiceLifetime.Host, "a", factory: _ => new Value("a2"))
            .AddKeyedEnumerableFactory<IValue, string>(ServiceLifetime.Host, "b", factory: _ => new Value("b"))
            .AddEnumerableFactory<IValue>(ServiceLifetime.Host, factory: _ => new Value("plain"))
            .Build();

        IValue a = await provider.ResolveKeyedAsync<IValue, string>("a");
        IValue b = await provider.ResolveKeyedAsync<IValue, string>("b");
        IValue[] allA = await provider.ResolveKeyedEnumerableAsync<IValue, string>("a");
        IValue[] plain = (await provider.ResolveAsync<IEnumerable<IValue>>()).ToArray();

        await Assert.That(a.Name).IsEqualTo("a2");
        await Assert.That(b.Name).IsEqualTo("b");
        await Assert.That(allA).Count().IsEqualTo(2);
        await Assert.That(allA[0].Name).IsEqualTo("a1");
        await Assert.That(allA[1].Name).IsEqualTo("a2");
        await Assert.That(plain).HasSingleItem();
        await Assert.That(plain[0].Name).IsEqualTo("plain");
    }

    [Test]
    public async Task KeyTypeIsPartOfIdentityAndKeyedSingletonsAreCachedIndependently() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddKeyedFactory<IValue, int>(ServiceLifetime.Host, 1, factory: _ => new Value("int"))
            .AddKeyedFactory<IValue, long>(ServiceLifetime.Host, 1L, factory: _ => new Value("long"))
            .Build();

        IValue first = await provider.ResolveKeyedAsync<IValue, int>(1);
        IValue second = await provider.ResolveKeyedAsync<IValue, int>(1);
        IValue other = await provider.ResolveKeyedAsync<IValue, long>(1L);

        await Assert.That(first).IsSameReferenceAs(second);
        await Assert.That(first).IsNotSameReferenceAs(other);
        await Assert.That(other.Name).IsEqualTo("long");
    }

    [Test]
    public async Task FactoryCanResolveAKeyedDependency() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddKeyedFactory<IValue, string>(ServiceLifetime.Host, "dependency", factory: _ => new Value("dependency"))
            .AddFactory<Consumer>(ServiceLifetime.Host, factory: resolver => new Consumer(resolver.GetKeyed<IValue, string>("dependency")))
            .Build();

        var consumer = await provider.ResolveAsync<Consumer>();
        await Assert.That(consumer.Value2.Name).IsEqualTo("dependency");
    }

    [Test]
    public async Task GeneratedConstructorCanResolveAConstantKey() {
        await using ServiceProvider provider = new ServiceCollection()
            .RegisterActivators<GeneratedKeyedConsumer>()
            .AddKeyedFactory<IValue, string>(ServiceLifetime.Host, "generated", factory: _ => new Value("generated"))
            .Build();

        var consumer = await provider.ResolveAsync<GeneratedKeyedConsumer>();
        await Assert.That(consumer.Value.Name).IsEqualTo("generated");
    }

    public interface IValue {
        string Name { get; }
    }

    private sealed class Value(string name) : IValue {
        public string Name { get; } = name;
    }

    private sealed class Consumer(IValue value) {
        public IValue Value2 { get; } = value;
    }
}

[TransientService<GeneratedKeyedConsumer>]
[UsedImplicitly]
public sealed class GeneratedKeyedConsumer([KeyedDependency<KeyedServiceTests.IValue, string>("generated")] KeyedServiceTests.IValue value) {
    public KeyedServiceTests.IValue Value { get; } = value;
}
