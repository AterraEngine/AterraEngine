using JetBrains.Annotations;

namespace AterraEngine.Core.DependencyInjection.Tests;

public sealed class OpenGenericClosureTests {
    // ReSharper disable once UnusedTypeParameter
    private interface IOpen<T>;
    private sealed class Open<T> : IOpen<T>;
    private sealed class PairDecorator(TestFixtures.IClosedPair<string, List<int>> inner)
        : TestFixtures.IClosedPair<string, List<int>> {
        [UsedImplicitly]
        public TestFixtures.IClosedPair<string, List<int>> Inner { get; } = inner;
    }

    [Test]
    public async Task NestedMultiArgumentClosuresHaveIndependentCachesAndDispose() {
        ServiceProvider provider = new ServiceCollection()
            .RegisterActivators<TestFixtures.ClosedPair<string, List<int>>>()
            .Build();

        var first = (TestFixtures.ClosedPair<string, List<int>>)
            await provider.ResolveAsync<TestFixtures.IClosedPair<string, List<int>>>();
        var second = await provider.ResolveAsync<TestFixtures.IClosedPair<string, List<int>>>();
        var other = await provider.ResolveAsync<TestFixtures.IClosedPair<List<string>, Dictionary<string, int>>>();

        await Assert.That(first).IsSameReferenceAs(second);
        await Assert.That(first).IsNotSameReferenceAs(other);
        await provider.DisposeAsync();
        await Assert.That(first.Disposed).IsTrue();
    }

    [Test]
    public async Task MissingClosedClosureIsAnOrdinaryBuildResolutionError() {
        await using ServiceProvider provider = new ServiceCollection().Build();

        // ReSharper disable once AccessToDisposedClosure
        await Assert.That(async () => await provider.ResolveAsync<TestFixtures.IClosedPair<int, int>>())
            .Throws<DependencyInjectionException>()
            .WithMessageContaining("Unregistered service");
    }

    [Test]
    public async Task ClosedGenericCycleIsValidatedByTheNormalBuildGraph() {
        ServiceCollection services = new ServiceCollection()
            .AddGeneratedActivator<TestFixtures.Cycle<string>>(
                static (ref resolver) =>
                    new TestFixtures.Cycle<string>(resolver.Get<TestFixtures.ICycle<string>>()),
                typeof(TestFixtures.ICycle<string>))
            .Add<TestFixtures.ICycle<string>, TestFixtures.Cycle<string>>(ServiceLifetime.Transient);

        await Assert.That(() => services.Build())
            .Throws<DependencyInjectionException>()
            .WithMessageContaining("cycle");
    }

    [Test]
    public async Task UnrestrictedOpenRegistrationFailsClearlyAtBuild() {
        ServiceCollection services = new ServiceCollection().Add(new ServiceRecord(
            ServiceLifetime.Transient, typeof(IOpen<>), typeof(Open<>)));

        await Assert.That(() => services.Build())
            .Throws<DependencyInjectionException>()
            .WithMessageContaining("closed");
    }

    [Test]
    public async Task ClosedClosureUsesTheExistingDecoratorPath() {
        ServiceCollection services = new ServiceCollection()
            .RegisterActivators<TestFixtures.ClosedPair<string, List<int>>>()
            .Decorate<TestFixtures.IClosedPair<string, List<int>>, PairDecorator>(
                inner => new PairDecorator(inner));
        await using ServiceProvider provider = services.Build();

        await Assert.That(await provider.ResolveAsync<TestFixtures.IClosedPair<string, List<int>>>())
            .IsTypeOf<PairDecorator>();
    }
}
