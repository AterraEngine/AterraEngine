// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public static class ServiceCollectionExtensions {
    extension(ServiceCollection services) {
        public ServiceCollection Add(ServiceRecord record)
            => services.AddRecord(record);

        public ServiceCollection AddEnumerable(ServiceRecord record)
            => services.AddEnumerableRecord(record);

        public ServiceCollection Add<T>(ServiceLifetime lifetime) where T : class
            => services.Add<T, T>(lifetime);

        public ServiceCollection Add<TService, TImplementation>(ServiceLifetime lifetime)
            where TImplementation : class, TService
            => services.Add(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation)));

        public ServiceCollection AddEnumerable<TService, TImplementation>(ServiceLifetime lifetime)
            where TImplementation : class, TService {
            services.AddGeneratedCollectionResolver<TService>(static (ref resolver) => resolver.GetAll<TService>());
            return services.AddEnumerable(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation)));
        }

        public ServiceCollection AddFactory<T>(ServiceLifetime lifetime, Func<IServiceResolver, T> factory) where T : class {
            ArgumentNullException.ThrowIfNull(factory);
            return services.AddRegistration(ServiceRegistration.AsFactory(new ServiceRecord(lifetime, typeof(T), typeof(T), services.Module), factory));
        }

        public ServiceCollection AddEnumerableFactory<T>(ServiceLifetime lifetime, Func<IServiceResolver, T> factory) where T : class {
            ArgumentNullException.ThrowIfNull(factory);
            services.AddGeneratedCollectionResolver<T>(static (ref resolver) => resolver.GetAll<T>());
            return services.AddOrAppendRegistration(ServiceRegistration.AsFactory(new ServiceRecord(lifetime, typeof(T), typeof(T), services.Module), factory));
        }

        public ServiceCollection AddInstance<T>(T instance, ServiceInstanceOwnership ownership) where T : class {
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

            return services.AddRegistration(ServiceRegistration.AsInstance(new ServiceRecord(ServiceLifetime.Singleton, typeof(T), instance.GetType(), services.Module), instance, ownership));
        }

        public ServiceCollection AddEnumerableInstance<T>(T instance, ServiceInstanceOwnership ownership) where T : class {
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

            services.AddGeneratedCollectionResolver<T>(static (ref resolver) => resolver.GetAll<T>());
            return services.AddOrAppendRegistration(ServiceRegistration.AsInstance(new ServiceRecord(ServiceLifetime.Singleton, typeof(T), instance.GetType(), services.Module), instance, ownership));
        }

        public ServiceCollection AddKeyed<TService, TImplementation, TKey>(ServiceLifetime lifetime, TKey key)
            where TImplementation : class, TService
            => services.RegisterKeyedRegistration(new ServiceRegistration(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation), services.Module), ServiceKey.Of<TService, TKey>(key)));

        public ServiceCollection AddKeyed<TService, TImplementation, TKey>(TKey key, ServiceLifetime lifetime)
            where TImplementation : class, TService => services.AddKeyed<TService, TImplementation, TKey>(lifetime, key);

        public ServiceCollection AddKeyed<TService, TImplementation>(ServiceLifetime lifetime, object? key)
            where TImplementation : class, TService => services.RegisterKeyedRegistration(new ServiceRegistration(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation), services.Module), ServiceKey.OfRuntime<TService>(key)));

        public ServiceCollection AddKeyed<TService, TImplementation>(object? key, ServiceLifetime lifetime)
            where TImplementation : class, TService => services.AddKeyed<TService, TImplementation>(lifetime, key);

        public ServiceCollection AddNamed<TService, TImplementation>(string name, ServiceLifetime lifetime)
            where TImplementation : class, TService => services.AddKeyed<TService, TImplementation, string>(lifetime, name);

        public ServiceCollection AddKeyedEnumerable<TService, TImplementation, TKey>(ServiceLifetime lifetime, TKey key)
            where TImplementation : class, TService => services.AddOrAppendKeyedRegistration(new ServiceRegistration(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation), services.Module), ServiceKey.Of<TService, TKey>(key)));

        public ServiceCollection AddKeyedEnumerable<TService, TImplementation>(ServiceLifetime lifetime, object? key)
            where TImplementation : class, TService => services.AddOrAppendKeyedRegistration(new ServiceRegistration(new ServiceRecord(lifetime, typeof(TService), typeof(TImplementation), services.Module), ServiceKey.OfRuntime<TService>(key)));

        public ServiceCollection AddNamedEnumerable<TService, TImplementation>(string name, ServiceLifetime lifetime)
            where TImplementation : class, TService => services.AddKeyedEnumerable<TService, TImplementation, string>(lifetime, name);

        public ServiceCollection AddKeyedFactory<TService, TKey>(ServiceLifetime lifetime, TKey key, Func<IServiceResolver, TService> factory)
            where TService : class {
            ArgumentNullException.ThrowIfNull(factory);
            return services.RegisterKeyedRegistration(ServiceRegistration.AsFactory(new ServiceRecord(lifetime, typeof(TService), typeof(TService), services.Module), factory, ServiceKey.Of<TService, TKey>(key)));
        }

        public ServiceCollection AddKeyedFactory<TService, TKey>(TKey key, ServiceLifetime lifetime, Func<IServiceResolver, TService> factory)
            where TService : class => services.AddKeyedFactory(lifetime, key, factory);

        public ServiceCollection AddNamedFactory<TService>(string name, ServiceLifetime lifetime, Func<IServiceResolver, TService> factory)
            where TService : class => services.AddKeyedFactory(lifetime, name, factory);

        public ServiceCollection AddKeyedEnumerableFactory<TService, TKey>(ServiceLifetime lifetime, TKey key, Func<IServiceResolver, TService> factory)
            where TService : class {
            ArgumentNullException.ThrowIfNull(factory);
            return services.AddOrAppendKeyedRegistration(ServiceRegistration.AsFactory(new ServiceRecord(lifetime, typeof(TService), typeof(TService), services.Module), factory, ServiceKey.Of<TService, TKey>(key)));
        }

        public ServiceCollection AddKeyedInstance<TService, TKey>(TKey key, TService instance, ServiceInstanceOwnership ownership)
            where TService : class {
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

            return services.RegisterKeyedRegistration(ServiceRegistration.AsInstance(new ServiceRecord(ServiceLifetime.Singleton, typeof(TService), instance.GetType(), services.Module), instance, ownership, ServiceKey.Of<TService, TKey>(key)));
        }

        public ServiceCollection AddKeyedEnumerableInstance<TService, TKey>(TKey key, TService instance, ServiceInstanceOwnership ownership)
            where TService : class {
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

            return services.AddOrAppendKeyedRegistration(ServiceRegistration.AsInstance(new ServiceRecord(ServiceLifetime.Singleton, typeof(TService), instance.GetType(), services.Module), instance, ownership, ServiceKey.Of<TService, TKey>(key)));
        }

        public ServiceCollection AddNamedInstance<TService>(string name, TService instance, ServiceInstanceOwnership ownership)
            where TService : class => services.AddKeyedInstance(name, instance, ownership);

        public ServiceCollection Decorate<TService, TDecorator>(Func<TService, TDecorator> decorator)
            where TService : class where TDecorator : class, TService {
            ArgumentNullException.ThrowIfNull(decorator);
            return services.DecorateCore(typeof(TService), null, typeof(TDecorator), factory: inner => decorator((TService)inner), null);
        }

        public ServiceCollection Decorate<TService, TDecorator>()
            where TService : class where TDecorator : class, TService => services.DecorateGeneratedCore<TService, TDecorator>(null);

        public ServiceCollection Decorate<TService, TDecorator, TKey>(TKey key, Func<TService, TDecorator> decorator)
            where TService : class where TDecorator : class, TService {
            ArgumentNullException.ThrowIfNull(decorator);
            return services.DecorateCore(typeof(TService), ServiceKey.Of<TService, TKey>(key), typeof(TDecorator), factory: inner => decorator((TService)inner), null);
        }

        public ServiceCollection Decorate<TService, TDecorator, TKey>(TKey key)
            where TService : class where TDecorator : class, TService => services.DecorateGeneratedCore<TService, TDecorator>(ServiceKey.Of<TService, TKey>(key));

        public ServiceCollection Decorate<TService, TDecorator>(GeneratedServiceActivator create, params Type[] dependencies)
            where TService : class where TDecorator : class, TService {
            ArgumentNullException.ThrowIfNull(create);
            ArgumentNullException.ThrowIfNull(dependencies);
            return services.DecorateCore(typeof(TService), null, typeof(TDecorator), null, new ServiceActivationPlan(null, create, dependencies.ToArray()));
        }
    }
}
