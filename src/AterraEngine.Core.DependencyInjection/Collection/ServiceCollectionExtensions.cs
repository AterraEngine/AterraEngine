// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public static class ServiceCollectionExtensions {
    extension(IServiceCollection services) {
        public IServiceCollection Add(ServiceRecord record)
            => services.Add(record);

        public IServiceCollection AddEnumerable(ServiceRecord record)
            => services.AddEnumerable(record);

        public IServiceCollection Add<T>(ServiceLifetime lifetime) where T : class
            => services.Add<T, T>(lifetime);

        public IServiceCollection Add<TService, TImplementation>(ServiceLifetime lifetime)
            where TImplementation : class, TService {
            ServiceRecord record = new(lifetime, typeof(TService), typeof(TImplementation));
            return services.Add(record);
        }

        public IServiceCollection AddEnumerable<TService, TImplementation>(ServiceLifetime lifetime)
            where TImplementation : class, TService {
            services.AddGeneratedCollectionResolver<TService>(static (ref resolver) => resolver.GetAll<TService>());
            ServiceRecord record = new(lifetime, typeof(TService), typeof(TImplementation));
            return services.AddEnumerable(record);
        }

        public IServiceCollection AddFactory<T>(ServiceLifetime lifetime, Func<IServiceResolver, T> factory) where T : class {
            ArgumentNullException.ThrowIfNull(factory);
            ServiceRecord record = new(lifetime, typeof(T), typeof(T));
            return services.AddFactory(record, FactoryAdapter, enumerable: false);

            T FactoryAdapter(IServiceResolver resolver) => factory(resolver);
        }

        public IServiceCollection AddEnumerableFactory<T>(ServiceLifetime lifetime, Func<IServiceResolver, T> factory) where T : class {
            ArgumentNullException.ThrowIfNull(factory);
            services.AddGeneratedCollectionResolver<T>(static (ref resolver) => resolver.GetAll<T>());
            ServiceRecord record = new(lifetime, typeof(T), typeof(T));
            return services.AddFactory(record, FactoryAdapter, enumerable: true);

            T FactoryAdapter(IServiceResolver resolver) => factory(resolver);
        }

        public IServiceCollection AddInstance<T>(T instance, ServiceInstanceOwnership ownership) where T : class {
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

            ServiceRecord record = new(ServiceLifetime.Singleton, typeof(T), instance.GetType());
            return services.AddInstance(record, instance, ownership, enumerable: false);
        }

        public IServiceCollection AddEnumerableInstance<T>(T instance, ServiceInstanceOwnership ownership) where T : class {
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

            services.AddGeneratedCollectionResolver<T>(static (ref resolver) => resolver.GetAll<T>());
            ServiceRecord record = new(ServiceLifetime.Singleton, typeof(T), instance.GetType());
            return services.AddInstance(record, instance, ownership, enumerable: true);
        }

        public IServiceCollection AddKeyed<TService, TImplementation, TKey>(ServiceLifetime lifetime, TKey key)
            where TImplementation : class, TService {
            ServiceRecord record = new(lifetime, typeof(TService), typeof(TImplementation));
            ServiceKey serviceKey = ServiceKey.Of<TService, TKey>(key);
            return services.AddKeyed(record, serviceKey, enumerable: false);
        }

        public IServiceCollection AddKeyed<TService, TImplementation, TKey>(TKey key, ServiceLifetime lifetime)
            where TImplementation : class, TService => services.AddKeyed<TService, TImplementation, TKey>(lifetime, key);

        public IServiceCollection AddKeyed<TService, TImplementation>(ServiceLifetime lifetime, object? key)
            where TImplementation : class, TService {
            ServiceRecord record = new(lifetime, typeof(TService), typeof(TImplementation));
            ServiceKey serviceKey = ServiceKey.OfRuntime<TService>(key);
            return services.AddKeyed(record, serviceKey, enumerable: false);
        }

        public IServiceCollection AddKeyed<TService, TImplementation>(object? key, ServiceLifetime lifetime)
            where TImplementation : class, TService => services.AddKeyed<TService, TImplementation>(lifetime, key);

        public IServiceCollection AddNamed<TService, TImplementation>(string name, ServiceLifetime lifetime)
            where TImplementation : class, TService => services.AddKeyed<TService, TImplementation, string>(lifetime, name);

        public IServiceCollection AddKeyedEnumerable<TService, TImplementation, TKey>(ServiceLifetime lifetime, TKey key)
            where TImplementation : class, TService {
            ServiceRecord record = new(lifetime, typeof(TService), typeof(TImplementation));
            ServiceKey serviceKey = ServiceKey.Of<TService, TKey>(key);
            return services.AddKeyed(record, serviceKey, enumerable: true);
        }

        public IServiceCollection AddKeyedEnumerable<TService, TImplementation>(ServiceLifetime lifetime, object? key)
            where TImplementation : class, TService {
            ServiceRecord record = new(lifetime, typeof(TService), typeof(TImplementation));
            ServiceKey serviceKey = ServiceKey.OfRuntime<TService>(key);
            return services.AddKeyed(record, serviceKey, enumerable: true);
        }

        public IServiceCollection AddNamedEnumerable<TService, TImplementation>(string name, ServiceLifetime lifetime)
            where TImplementation : class, TService => services.AddKeyedEnumerable<TService, TImplementation, string>(lifetime, name);

        public IServiceCollection AddKeyedFactory<TService, TKey>(ServiceLifetime lifetime, TKey key, Func<IServiceResolver, TService> factory)
            where TService : class {
            ArgumentNullException.ThrowIfNull(factory);
            ServiceRecord record = new(lifetime, typeof(TService), typeof(TService));
            ServiceKey serviceKey = ServiceKey.Of<TService, TKey>(key);
            Func<IServiceResolver, object> factoryAdapter = resolver => factory(resolver);
            return services.AddKeyedFactory(record, serviceKey, factoryAdapter, enumerable: false);
        }

        public IServiceCollection AddKeyedFactory<TService, TKey>(TKey key, ServiceLifetime lifetime, Func<IServiceResolver, TService> factory)
            where TService : class => services.AddKeyedFactory(lifetime, key, factory);

        public IServiceCollection AddNamedFactory<TService>(string name, ServiceLifetime lifetime, Func<IServiceResolver, TService> factory)
            where TService : class => services.AddKeyedFactory(lifetime, name, factory);

        public IServiceCollection AddKeyedEnumerableFactory<TService, TKey>(ServiceLifetime lifetime, TKey key, Func<IServiceResolver, TService> factory)
            where TService : class {
            ArgumentNullException.ThrowIfNull(factory);
            ServiceRecord record = new(lifetime, typeof(TService), typeof(TService));
            ServiceKey serviceKey = ServiceKey.Of<TService, TKey>(key);
            services.AddKeyedFactory(record, serviceKey, FactoryAdapter, enumerable: true);
            return services;

            TService FactoryAdapter(IServiceResolver resolver) => factory(resolver);
        }

        public IServiceCollection AddKeyedInstance<TService, TKey>(TKey key, TService instance, ServiceInstanceOwnership ownership)
            where TService : class {
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

            ServiceRecord record = new(ServiceLifetime.Singleton, typeof(TService), instance.GetType());
            ServiceKey serviceKey = ServiceKey.Of<TService, TKey>(key);
            return services.AddKeyedInstance(record, serviceKey, instance, ownership, enumerable: false);
        }

        public IServiceCollection AddKeyedEnumerableInstance<TService, TKey>(TKey key, TService instance, ServiceInstanceOwnership ownership)
            where TService : class {
            ArgumentNullException.ThrowIfNull(instance);
            if (!Enum.IsDefined(ownership)) throw new ArgumentOutOfRangeException(nameof(ownership));

            ServiceRecord record = new(ServiceLifetime.Singleton, typeof(TService), instance.GetType());
            ServiceKey serviceKey = ServiceKey.Of<TService, TKey>(key);
            return services.AddKeyedInstance(record, serviceKey, instance, ownership, enumerable: true);
        }

        public IServiceCollection AddNamedInstance<TService>(string name, TService instance, ServiceInstanceOwnership ownership)
            where TService : class => services.AddKeyedInstance(name, instance, ownership);

        public IServiceCollection Decorate<TService, TDecorator>(Func<TService, TDecorator> decorator)
            where TService : class where TDecorator : class, TService {
            ArgumentNullException.ThrowIfNull(decorator);
            return services.Decorate(typeof(TService), null, typeof(TDecorator), Factory, null, []);

            TDecorator Factory(object inner) => decorator((TService)inner);
        }

        public IServiceCollection Decorate<TService, TDecorator>()
            where TService : class where TDecorator : class, TService => services.DecorateGenerated<TService, TDecorator>(null);

        public IServiceCollection Decorate<TService, TDecorator, TKey>(TKey key, Func<TService, TDecorator> decorator)
            where TService : class where TDecorator : class, TService {
            ArgumentNullException.ThrowIfNull(decorator);
            ServiceKey serviceKey = ServiceKey.Of<TService, TKey>(key);
            Func<object, object> factory = inner => decorator((TService)inner);
            return services.Decorate(typeof(TService), serviceKey, typeof(TDecorator), factory, null, []);
        }

        public IServiceCollection Decorate<TService, TDecorator, TKey>(TKey key)
            where TService : class where TDecorator : class, TService {
            ServiceKey serviceKey = ServiceKey.Of<TService, TKey>(key);
            return services.DecorateGenerated<TService, TDecorator>(serviceKey);
        }

        public IServiceCollection Decorate<TService, TDecorator>(GeneratedServiceActivator create, params Type[] dependencies)
            where TService : class where TDecorator : class, TService {
            ArgumentNullException.ThrowIfNull(create);
            ArgumentNullException.ThrowIfNull(dependencies);
            Type[] dependencyCopy = dependencies.ToArray();
            return services.Decorate(typeof(TService), null, typeof(TDecorator), null, create, dependencyCopy);
        }
    }
}
