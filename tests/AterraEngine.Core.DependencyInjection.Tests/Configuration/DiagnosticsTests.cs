using AterraEngine.Core.DependencyInjection.Tests.Generation;

namespace AterraEngine.Core.DependencyInjection.Tests.Configuration;
public sealed class DiagnosticsTests {

    [Test]
    public async Task DiagnosticsAreSilentByDefault() {
        await using ServiceProvider provider = new ServiceCollection()
            .AddFactory<TracedService>(ServiceLifetime.Host, factory: _ => new TracedService())
            .Build();

        await Assert.That(await provider.ResolveAsync<TracedService>()).IsNotNull();
    }

    [Test]
    public async Task DiagnosticsCaptureActivationCacheScopeAndCleanupEvents() {
        var events = new List<ServiceDiagnosticEvent>();
        await using ServiceProvider provider = new ServiceCollection()
            .ConfigureDiagnostics(new ServiceDiagnosticsOptions(new ServiceDiagnosticSink(events.Add), true))
            .AddFactory<TracedService>(ServiceLifetime.Host, factory: _ => new TracedService())
            .AddFactory<TracedWorldService>(ServiceLifetime.Of<AterraWorld>(), factory: _ => new TracedWorldService())
            .Build();

        await provider.ResolveAsync<TracedService>();
        await provider.ResolveAsync<TracedService>();
        await using ServiceProvider generatedProvider = new ServiceCollection()
            .ConfigureDiagnostics(new ServiceDiagnosticsOptions(new ServiceDiagnosticSink(events.Add)))
            .RegisterActivators<GeneratedRegistrationTests>()
            .Build();
        await generatedProvider.ResolveAsync<IGeneratedClock>();
        OwnedServiceScope world = provider.CreateScope<AterraWorld>();
        await world.ResolveAsync<TracedWorldService>();
        await world.DisposeAsync();

        await Assert.That(events.Any(item => item is { Kind: ServiceDiagnosticEventKind.ActivationStarted, ActivationSource: ServiceDiagnosticActivationSource.Factory } &&
            item.ServiceType == typeof(TracedService))).IsTrue();
        await Assert.That(events.Any(item => item is { Kind: ServiceDiagnosticEventKind.ActivationStarted, ActivationSource: ServiceDiagnosticActivationSource.Generated } &&
            item.ServiceType == typeof(IGeneratedClock))).IsTrue();
        await Assert.That(events.Any(item => item.Kind == ServiceDiagnosticEventKind.CacheHit &&
            item.ServiceType == typeof(TracedService))).IsTrue();
        await Assert.That(events.Any(item => item.Kind == ServiceDiagnosticEventKind.ScopeCreated &&
            item.ScopeType == typeof(AterraWorld))).IsTrue();
        await Assert.That(events.Any(item => item.Kind == ServiceDiagnosticEventKind.CleanupCompleted)).IsTrue();
        await Assert.That(events.Where(item => item.Kind == ServiceDiagnosticEventKind.ActivationCompleted)
            .Where(item => item.ServiceType == typeof(TracedService))
            .All(item => item.Duration >= TimeSpan.Zero && item.AllocatedBytes is not null)).IsTrue();
    }

    [Test]
    public async Task DiagnosticFailureDoesNotChangeExceptionOrInnerException() {
        var cause = new InvalidOperationException("diagnostic-cause");
        var events = new List<ServiceDiagnosticEvent>();
        await using ServiceProvider provider = new ServiceCollection()
            .ConfigureDiagnostics(new ServiceDiagnosticsOptions(new ServiceDiagnosticSink(events.Add)))
            .AddFactory<BrokenService>(ServiceLifetime.Host, factory: _ => throw cause)
            .Build();

        // ReSharper disable once AccessToDisposedClosure
        var error = await Assert.That(async () => await provider.ResolveAsync<BrokenService>())
            .ThrowsExactly<DependencyInjectionException>();

        await Assert.That(error!.InnerException).IsSameReferenceAs(cause);
        await Assert.That(events.Any(item => item is { Kind: ServiceDiagnosticEventKind.ActivationFailed, Error: not null } && item.ServiceType == typeof(BrokenService))).IsTrue();
    }

    private sealed class TracedService;

    private sealed class TracedWorldService;

    private sealed class BrokenService;
}
