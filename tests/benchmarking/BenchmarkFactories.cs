// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using Microsoft.Extensions.DependencyInjection;
using AterraServiceCollection = AterraEngine.Core.DependencyInjection.IServiceCollection;
using MicrosoftServiceCollection = Microsoft.Extensions.DependencyInjection.IServiceCollection;

namespace AterraEngine.Core.DependencyInjection.Benchmarks;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal static class BenchmarkFactories {
    public const string KeyedKey = "benchmark";
    public static ServiceProviderOptions ProviderOptions => new() { ValidateOnBuild = true, ValidateScopes = true };

    public static AterraServiceCollection CreateAterraServices() => new ServiceCollection().RegisterActivators<CoreResolutionBenchmarks>()
        .AddGeneratedCollectionResolver<string>(static (ref resolver) => resolver.GetAll<string>());

    public static MicrosoftServiceCollection CreateMicrosoftServices() => new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        .AddSingleton<SingletonService>().AddTransient<TransientService>().AddScoped<ScopedService>()
        .AddTransient<Chain1>().AddTransient<Chain2>().AddTransient<Chain3>().AddTransient<Chain4>()
        .AddTransient<Chain5>().AddTransient<Chain6>().AddTransient<Chain7>().AddTransient<Chain8>()
        .AddTransient<GraphLeaf>().AddTransient<LeftBranch>().AddTransient<RightBranch>().AddTransient<RequestHandler>()
        .AddTransient<DiagnosticService>().AddSingleton<IClosedBenchmark<string>, ClosedBenchmark<string>>();

    public static AterraServiceCollection CreateAterraCollectionServices(int count) {
        AterraServiceCollection services = CreateAterraServices();
        for (int index = 0; index < count; index++) {
            services.AddEnumerable<ICollectionItem, CollectionItem>(ServiceLifetime.Transient);
        }

        return services;
    }

    public static MicrosoftServiceCollection CreateMicrosoftCollectionServices(int count) {
        MicrosoftServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        for (int index = 0; index < count; index++) {
            services.AddTransient<ICollectionItem, CollectionItem>();
        }

        return services;
    }

    public static AterraServiceCollection CreateAterraKeyedServices() => CreateAterraServices()
        .AddKeyed<KeyedSingleton, KeyedSingleton, string>(ServiceLifetime.Singleton, KeyedKey)
        .AddKeyed<KeyedTransient, KeyedTransient, string>(ServiceLifetime.Transient, KeyedKey)
        .AddKeyedEnumerable<IKeyedItem, KeyedCollectionItem, string>(ServiceLifetime.Transient, KeyedKey);

    public static MicrosoftServiceCollection CreateMicrosoftKeyedServices() => new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        .AddKeyedSingleton<KeyedSingleton>(KeyedKey).AddKeyedTransient<KeyedTransient>(KeyedKey)
        .AddKeyedTransient<IKeyedItem, KeyedCollectionItem>(KeyedKey);

    public static AterraServiceCollection CreateAterraDecoratedServices(int count) {
        AterraServiceCollection services = CreateAterraServices().Add<IDecoratedService, DecoratedService>(ServiceLifetime.Transient).Decorate<IDecoratedService, DecoratorOne>();
        if (count == 3) services.Decorate<IDecoratedService, DecoratorTwo>().Decorate<IDecoratedService, DecoratorThree>();
        return services;
    }

    public static MicrosoftServiceCollection CreateMicrosoftDecoratedServices(int count) {
        MicrosoftServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddTransient<DecoratedService>();
        if (count == 1) services.AddTransient<IDecoratedService>(static provider => new DecoratorOne(provider.GetRequiredService<DecoratedService>()));
        else services.AddTransient<IDecoratedService>(static provider => new DecoratorThree(new DecoratorTwo(new DecoratorOne(provider.GetRequiredService<DecoratedService>()))));
        return services;
    }

    public static AterraServiceCollection CreateAterraDisposalServices() => CreateAterraServices()
        .Add<SyncDisposable>(ServiceLifetime.Of<AterraWorld>()).Add<AsyncDisposable>(ServiceLifetime.Of<AterraWorld>()).Add<DualDisposable>(ServiceLifetime.Of<AterraWorld>());
    public static MicrosoftServiceCollection CreateMicrosoftDisposalServices() => new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        .AddScoped<SyncDisposable>().AddScoped<AsyncDisposable>().AddScoped<DualDisposable>().AddScoped<DisposableTransient>();

    public static AterraServiceCollection CreateAterraDiagnosticsServices(ServiceDiagnosticsOptions? options) {
        AterraServiceCollection services = CreateAterraServices().Add<DiagnosticService>(ServiceLifetime.Transient);
        return options is null ? services : services.ConfigureDiagnostics(options);
    }

    public static T Resolve<T>(ServiceProvider provider) where T : notnull => provider.ResolveAsync<T>().GetAwaiter().GetResult();
    public static T Resolve<T>(OwnedServiceScope scope) where T : notnull => scope.ResolveAsync<T>().GetAwaiter().GetResult();
    public static T ResolveKeyed<T>(ServiceProvider provider) where T : notnull => provider.ResolveKeyedAsync<T, string>(KeyedKey).GetAwaiter().GetResult();
}
