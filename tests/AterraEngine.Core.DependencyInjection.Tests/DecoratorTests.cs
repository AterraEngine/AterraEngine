namespace AterraEngine.Core.DependencyInjection.Tests;

public sealed class DecoratorTests {
    [Test]
    public async Task DecoratesEveryUnkeyedRegistrationAndLeavesRegistrationOrderIntact() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddEnumerableFactory<IValue>(ServiceLifetime.Host, _ => new Value("a"))
            .AddEnumerableFactory<IValue>(ServiceLifetime.Host, _ => new Value("b"))
            .Decorate<IValue, ValueDecorator>((inner) => new ValueDecorator(inner))
            .Build();

        IValue[] values = (await provider.ResolveAsync<IEnumerable<IValue>>()).ToArray();
        await Assert.That(values.Select(value => value.Name)).IsEquivalentTo(["decorated:a", "decorated:b"]);
        await Assert.That((await provider.ResolveAsync<IValue>()).Name).IsEqualTo("decorated:b");
    }

    [Test]
    public async Task KeyedDecorationTargetsOnlyTheExactKeyAndPreservesOriginalLifetime() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddKeyedFactory<IValue, string>(ServiceLifetime.Host, "a", _ => new Value("a"))
            .AddKeyedFactory<IValue, string>(ServiceLifetime.Host, "b", _ => new Value("b"))
            .Decorate<IValue, ValueDecorator, string>("a", inner => new ValueDecorator(inner))
            .Build();

        IValue first = await provider.ResolveKeyedAsync<IValue, string>("a");
        IValue second = await provider.ResolveKeyedAsync<IValue, string>("a");
        IValue other = await provider.ResolveKeyedAsync<IValue, string>("b");
        await Assert.That(first).IsSameReferenceAs(second);
        await Assert.That(first.Name).IsEqualTo("decorated:a");
        await Assert.That(other.Name).IsEqualTo("b");
    }

    [Test]
    public async Task GeneratedDecoratorGetsInnerWithoutResolvingThePublicKey() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<IValue>(ServiceLifetime.Host, _ => new Value("inner"))
            .AddGeneratedActivator<ValueDecorator>(static (ref resolver) =>
                new ValueDecorator(resolver.GetInner<IValue>()))
            .Decorate<IValue, ValueDecorator>()
            .Build();

        var value = await provider.ResolveAsync<IValue>();
        await Assert.That(value.Name).IsEqualTo("decorated:inner");
    }

    [Test]
    public async Task GeneratedDecoratorsCanBeChainedThreeDeep() {
        await using ServiceProvider provider = new ServiceCollection()
            .RegisterActivators<DecoratorTests>()
            .Add<IGeneratedValue, GeneratedValue>(ServiceLifetime.Transient)
            .Decorate<IGeneratedValue, GeneratedDecoratorOne>()
            .Decorate<IGeneratedValue, GeneratedDecoratorTwo>()
            .Decorate<IGeneratedValue, GeneratedDecoratorThree>()
            .Build();

        IGeneratedValue value = await provider.ResolveAsync<IGeneratedValue>();

        await Assert.That(value.Name).IsEqualTo("three:two:one:value");
    }

    [Test]
    public async Task DecorationRequiresAnExistingRegistrationAndRejectsOpenGenericTypes() {
        await Assert.That(() => new ServiceCollection().Decorate<IValue, ValueDecorator>(inner => new ValueDecorator(inner)))
            .ThrowsExactly<DependencyInjectionException>().WithMessageContaining("unregistered");
    }

    public interface IValue { string Name { get; } }
    private sealed class Value(string name) : IValue { public string Name { get; } = name; }
    private sealed class ValueDecorator(IValue inner) : IValue { public string Name => "decorated:" + inner.Name; }

    public interface IGeneratedValue { string Name { get; } }

    [TransientService<GeneratedValue>]
    public sealed class GeneratedValue : IGeneratedValue { public string Name => "value"; }

    [TransientService<GeneratedDecoratorOne>]
    public sealed class GeneratedDecoratorOne([DecoratedDependency<IGeneratedValue>] IGeneratedValue inner) : IGeneratedValue {
        public string Name => "one:" + inner.Name;
    }

    [TransientService<GeneratedDecoratorTwo>]
    public sealed class GeneratedDecoratorTwo([DecoratedDependency<IGeneratedValue>] IGeneratedValue inner) : IGeneratedValue {
        public string Name => "two:" + inner.Name;
    }

    [TransientService<GeneratedDecoratorThree>]
    public sealed class GeneratedDecoratorThree([DecoratedDependency<IGeneratedValue>] IGeneratedValue inner) : IGeneratedValue {
        public string Name => "three:" + inner.Name;
    }
}
