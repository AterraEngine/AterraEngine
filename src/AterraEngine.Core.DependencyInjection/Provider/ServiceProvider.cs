// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
/// <summary>An engine singleton root with a primary Host scope. Shutdown and failed-activation cleanup are asynchronous.</summary>
public sealed class ServiceProvider : IDisposable, IAsyncDisposable, IServiceProvider, IServiceScopeFactory {
    private static readonly List<Exception> EmptyCleanupErrors = [];
    [ThreadStatic]
    private static ServiceProvider? _activatingProvider;
    [ThreadStatic]
    private static int _activationDepth;
    [ThreadStatic]
    private static ServiceResolutionContext? _spareContexts;
    private readonly Dictionary<object, long> _claimed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, GeneratedServiceCollectionResolver> _collectionResolvers;
    private readonly ServiceDiagnosticsOptions? _diagnostics;
    private readonly Dictionary<object, int> _external = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ServiceKey, ServiceRegistration[]> _keyedRegistrationSets;
    private readonly Dictionary<ServiceKey, ServiceRegistration> _keyedRegistrations;
    private readonly Dictionary<Type, ServiceRegistration[]> _registrationSets;
    private readonly Dictionary<Type, ServiceRegistration> _registrations;
    private long _constructionOrder;
    private long _diagnosticSequence;

    // -----------------------------------------------------------------------------------------------------------------
    // Constructors
    // -----------------------------------------------------------------------------------------------------------------
    internal ServiceProvider(
        Dictionary<Type, ServiceRegistration> registrations,
        Dictionary<Type, ServiceRegistration[]> registrationSets,
        Dictionary<ServiceKey, ServiceRegistration> keyedRegistrations,
        Dictionary<ServiceKey, ServiceRegistration[]> keyedRegistrationSets,
        Dictionary<Type, GeneratedServiceCollectionResolver> collectionResolvers,
        Dictionary<Type, Type[]> parents,
        Dictionary<Type, Type> inputOwners,
        ServiceScopeInput[] inputs,
        ServiceDiagnosticsOptions? diagnostics
    ) {
        _registrations = registrations;
        _registrationSets = registrationSets;
        _keyedRegistrations = keyedRegistrations;
        _keyedRegistrationSets = keyedRegistrationSets;
        _collectionResolvers = collectionResolvers;
        Parents = parents;
        InputOwners = inputOwners;
        _diagnostics = diagnostics;
        ArgumentNullException.ThrowIfNull(inputs);
        foreach (ServiceScopeInput input in inputs) {
            ArgumentNullException.ThrowIfNull(input);
            if (!InputOwners.TryGetValue(input.Type, out Type? owner) || owner != typeof(AterraSingleton) && owner != typeof(AterraHost))
                throw new DependencyInjectionException(
                    $"Input {input.Type} is not declared for {nameof(AterraSingleton)} or {nameof(AterraHost)}.");
        }

        ServiceScopeInput[] singletonInputs = inputs.Where(input => InputOwners[input.Type] == typeof(AterraSingleton)).ToArray();
        ServiceScopeInput[] hostInputs = inputs.Where(input => InputOwners[input.Type] == typeof(AterraHost)).ToArray();
        Singleton = new OwnedServiceScope(this, typeof(AterraSingleton), null, ValidateInputs(typeof(AterraSingleton), singletonInputs));
        Host = Singleton.CreateScope<AterraHost>(hostInputs);
        HashSet<ServiceRegistration> seenRegistrations = [];
        foreach (ServiceRegistration registration in registrations.Values) {
            ValidateInstanceInput(registration, seenRegistrations, inputs);
        }

        foreach (ServiceRegistration[] values in registrationSets.Values)
        foreach (ServiceRegistration registration in values) {
            ValidateInstanceInput(registration, seenRegistrations, inputs);
        }

        foreach (ServiceRegistration registration in keyedRegistrations.Values) {
            ValidateInstanceInput(registration, seenRegistrations, inputs);
        }

        foreach (ServiceRegistration[] values in keyedRegistrationSets.Values)
        foreach (ServiceRegistration registration in values) {
            ValidateInstanceInput(registration, seenRegistrations, inputs);
        }

        foreach (ServiceRegistration registration in seenRegistrations) {
            if (registration.Instance is not {} instance) continue;

            _claimed.Add(instance, ++_constructionOrder);
            if (registration.Ownership == ServiceInstanceOwnership.Container && IsDisposable(instance)) Singleton.AddOwned(instance);
        }
    }

    internal Lock Gate { get; } = new();
    internal Dictionary<Type, Type[]> Parents { get; }
    private Dictionary<Type, Type> InputOwners { get; }
    public OwnedServiceScope Singleton { get; }
    public OwnedServiceScope Host { get; }
    public ValueTask DisposeAsync() => Singleton.DisposeAsync();

    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    public void Dispose() => Singleton.Dispose();
    public object? GetService(Type serviceType) {
        ArgumentNullException.ThrowIfNull(serviceType);
        bool collection = serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
            _collectionResolvers.ContainsKey(serviceType.GetGenericArguments()[0]);
        if (!IsProviderService(serviceType) && !InputOwners.ContainsKey(serviceType) && !_registrations.ContainsKey(serviceType) && !collection) return null;

        return Host.ResolveAsync(serviceType).GetAwaiter().GetResult();
    }
    IServiceScope IServiceScopeFactory.CreateScope() => CreateScope();
    IServiceScope IServiceScopeFactory.CreateScope(Type scopeType, params ServiceScopeInput[] inputs) => CreateScope(scopeType, inputs);
    IServiceScope IServiceScopeFactory.CreateAsyncScope() => CreateAsyncScope();
    IServiceScope IServiceScopeFactory.CreateAsyncScope(Type scopeType, params ServiceScopeInput[] inputs) => CreateAsyncScope(scopeType, inputs);

    private void ValidateInstanceInput(
        ServiceRegistration registration,
        HashSet<ServiceRegistration> seenRegistrations,
        ServiceScopeInput[] inputs
    ) {
        if (!seenRegistrations.Add(registration)) return;

        if (registration.Instance is {} instance && inputs.Any(input => ReferenceEquals(input.Value, instance)))
            throw registration.Error("An external service cannot also be registered as a scope input.");
    }

    public ValueTask<T> ResolveAsync<T>() where T : notnull => Host.ResolveAsync<T>();
    public ValueTask<T> ResolveKeyedAsync<T, TKey>(TKey key) where T : notnull => AwaitKeyed<T>(ResolveAsync(Host, ServiceKey.Of<T, TKey>(key)));
    public ValueTask<T> ResolveKeyedAsync<T>(object? key) where T : notnull => AwaitKeyed<T>(ResolveAsync(Host, RuntimeKey(typeof(T), key)));
    public ValueTask<T> ResolveNamedAsync<T>(string name) where T : notnull => ResolveKeyedAsync<T, string>(name);
    public ValueTask<T[]> ResolveKeyedEnumerableAsync<T, TKey>(TKey key) => ResolveKeyedCollectionAsync<T, TKey>(Host, key);
    private static async ValueTask<T> AwaitKeyed<T>(ValueTask<object> result) where T : notnull => (T)await result.ConfigureAwait(false);
    public OwnedServiceScope CreateScope<TScope>(params ServiceScopeInput[] inputs) => CreateScope(typeof(TScope), inputs);
    public OwnedServiceScope CreateScope(Type scopeType, params ServiceScopeInput[] inputs) => Host.CreateScope(scopeType, inputs);
    public OwnedServiceScope CreateScope() => CreateScope<AterraWorld>();
    public OwnedServiceScope CreateAsyncScope() => CreateScope();
    public OwnedServiceScope CreateAsyncScope(Type scopeType, params ServiceScopeInput[] inputs) => CreateScope(scopeType, inputs);

    internal object? GetService(OwnedServiceScope scope, Type serviceType) {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(IServiceProvider)) return scope;
        if (serviceType == typeof(ServiceProvider)) return this;

        bool collection = serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
            _collectionResolvers.ContainsKey(serviceType.GetGenericArguments()[0]);
        if (!InputOwners.ContainsKey(serviceType) && !_registrations.ContainsKey(serviceType) && !collection) return null;

        return scope.ResolveAsync(serviceType).GetAwaiter().GetResult();
    }

    public object? GetKeyedService(Type serviceType, object? key) {
        ArgumentNullException.ThrowIfNull(serviceType);
        ServiceKey serviceKey = RuntimeKey(serviceType, key);
        if (!_keyedRegistrations.ContainsKey(serviceKey)) return null;

        return ResolveAsync(Host, serviceKey).GetAwaiter().GetResult();
    }

    private ServiceKey RuntimeKey(Type serviceType, object? key) {
        if (key is not null) return new ServiceKey(serviceType, key.GetType(), key);

        ServiceKey[] nullKeys = _keyedRegistrations.Keys.Where(candidate => candidate.ServiceType == serviceType && candidate.Value is null).ToArray();
        if (nullKeys.Length == 1) return nullKeys[0];

        if (nullKeys.Length > 1) throw new DependencyInjectionException($"Null keyed service lookup for {serviceType} is ambiguous; use the typed key overload.");

        return new ServiceKey(serviceType, typeof(object), null);
    }

    internal Dictionary<Type, object>? ValidateInputs(Type scope, ServiceScopeInput[] inputs) {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Length == 0) {
            foreach ((Type input, Type owner) in InputOwners) {
                if (owner == scope) throw new DependencyInjectionException($"Required input {input} is missing for {scope}.");
            }

            return null;
        }

        var values = new Dictionary<Type, object>();
        foreach (ServiceScopeInput input in inputs) {
            ArgumentNullException.ThrowIfNull(input);
            if (!InputOwners.TryGetValue(input.Type, out Type? owner) || owner != scope)
                throw new DependencyInjectionException($"Input {input.Type} is not declared for {scope}.");
            if (!values.TryAdd(input.Type, input.Value)) throw new DependencyInjectionException($"Duplicate input {input.Type}.");
        }

        foreach ((Type input, Type owner) in InputOwners) {
            if (owner == scope && !values.ContainsKey(input)) throw new DependencyInjectionException($"Required input {input} is missing for {scope}.");
        }

        return values;
    }

    internal ValueTask<object> ResolveAsync(OwnedServiceScope scope, Type service) {
        ArgumentNullException.ThrowIfNull(service);
        scope.Enter();
        try {
            if (TryResolveWithoutActivation(scope, service, out object cached)) {
                Emit(ServiceDiagnosticEventKind.CacheHit, service, scope, null, null, null, null, 0, 0);
                scope.Exit();
                return new ValueTask<object>(cached);
            }

            ServiceResolutionContext context = RentContext();
            ServiceCacheEntry? faultedEntry = null;
            try {
                object? result = Resolve(service, context, scope, null, true, out Task<ServiceOutcome>? pending, out faultedEntry);
                if (pending is not null) {
                    ReturnContext(context);
                    return AwaitCachedResolutionAsync(scope, pending);
                }

                context.CommitResources(scope, 0);
                ReturnContext(context);
                scope.Exit();
                return new ValueTask<object>(result!);
            }
            catch (Exception exception) {
                context.FailResources(0);
                if (context.Failed.Count == 0) {
                    faultedEntry?.SetOutcome(new ServiceFailure(exception));
                    ReturnContext(context);
                    scope.Exit();
                    return ValueTask.FromException<object>(exception);
                }

                List<object> failed = context.TakeFailed();
                ReturnContext(context);
                return CompleteFailedResolutionAsync(scope, failed, exception, faultedEntry);
            }
        }
        catch (Exception exception) {
            scope.Exit();
            return ValueTask.FromException<object>(exception);
        }
    }

    internal ValueTask<object> ResolveAsync(OwnedServiceScope scope, ServiceKey key) {
        ArgumentNullException.ThrowIfNull(key.ServiceType);
        scope.Enter();
        try {
            ServiceResolutionContext context = RentContext();
            ServiceCacheEntry? faultedEntry = null;
            try {
                object? result = ResolveKeyed(key, context, scope, null, true, out Task<ServiceOutcome>? pending, out faultedEntry);
                if (pending is not null) {
                    ReturnContext(context);
                    return AwaitCachedResolutionAsync(scope, pending);
                }

                context.CommitResources(scope, 0);
                ReturnContext(context);
                scope.Exit();
                return new ValueTask<object>(result!);
            }
            catch (Exception exception) {
                context.FailResources(0);
                if (context.Failed.Count == 0) {
                    faultedEntry?.SetOutcome(new ServiceFailure(exception));
                    ReturnContext(context);
                    scope.Exit();
                    return ValueTask.FromException<object>(exception);
                }

                List<object> failed = context.TakeFailed();
                ReturnContext(context);
                return CompleteFailedResolutionAsync(scope, failed, exception, faultedEntry);
            }
        }
        catch (Exception exception) {
            scope.Exit();
            return ValueTask.FromException<object>(exception);
        }
    }

    internal ValueTask<T[]> ResolveKeyedCollectionAsync<T, TKey>(OwnedServiceScope scope, TKey key) {
        ServiceKey serviceKey = ServiceKey.Of<T, TKey>(key);
        scope.Enter();
        ServiceResolutionContext context = RentContext();
        try {
            ServiceRegistration[] registrations = _keyedRegistrationSets.GetValueOrDefault(serviceKey) ?? Array.Empty<ServiceRegistration>();
            var values = new T[registrations.Length];
            for (int index = 0; index < registrations.Length; index++) {
                values[index] = (T)ResolveRegistration(registrations[index], typeof(T), context, scope, null, false, out _, out _)!;
            }

            context.CommitResources(scope, 0);
            ReturnContext(context);
            scope.Exit();
            return new ValueTask<T[]>(values);
        }
        catch (Exception exception) {
            context.FailResources(0);
            if (context.Failed.Count != 0) {
                List<object> failed = context.TakeFailed();
                ReturnContext(context);
                return AwaitTypedFailure<T>(CompleteFailedResolutionAsync(scope, failed, exception, null));
            }

            ReturnContext(context);
            scope.Exit();
            return ValueTask.FromException<T[]>(exception);
        }
    }

    private static async ValueTask<T[]> AwaitTypedFailure<T>(ValueTask<object> failure) => (T[])await failure.ConfigureAwait(false);

    internal object ResolveGenerated(Type service, ServiceResolutionContext context, OwnedServiceScope anchor, ServiceCacheEntry? cacheEntry)
        => Resolve(service, context, anchor, cacheEntry);

    internal object ResolveGeneratedKeyed(ServiceKey key, ServiceResolutionContext context, OwnedServiceScope anchor, ServiceCacheEntry? cacheEntry)
        => ResolveKeyed(key, context, anchor, cacheEntry, false, out _, out _)!;

    internal object ResolveGeneratedInner<T>(IServiceRegistration? inner, ServiceResolutionContext context, OwnedServiceScope anchor, ServiceCacheEntry? cacheEntry)
        where T : notnull {
        if (inner is null || inner.Record.Service != typeof(T))
            throw new DependencyInjectionException($"Generated decorator requested an invalid inner service {typeof(T)}.");

        return ResolveRegistration(inner, typeof(T), context, anchor, cacheEntry, false, out _, out _)!;
    }

    internal object Resolve(
        Type service,
        ServiceResolutionContext context,
        OwnedServiceScope callerAnchor,
        ServiceCacheEntry? callerEntry
    ) => Resolve(service, context, callerAnchor, callerEntry, false, out _, out _)!;

    private object? Resolve(
        Type service,
        ServiceResolutionContext context,
        OwnedServiceScope callerAnchor,
        ServiceCacheEntry? callerEntry,
        bool deferCachedWait,
        out Task<ServiceOutcome>? pending,
        out ServiceCacheEntry? faultedEntry
    ) {
        pending = null;
        faultedEntry = null;
        if (IsProviderService(service)) return ResolveProviderService(callerAnchor, service);

        if (service.IsGenericType && service.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return ResolveGeneratedCollectionObject(service.GetGenericArguments()[0], context, callerAnchor, callerEntry);

        if (InputOwners.TryGetValue(service, out Type? inputOwner)) {
            OwnedServiceScope owner = FindInputOwner(callerAnchor, inputOwner, service);
            return owner.Inputs![service];
        }

        if (!_registrations.TryGetValue(service, out ServiceRegistration? registration))
            throw new DependencyInjectionException($"Unregistered service {service}; path: {context.PathText}.");

        return ResolveRegistration(registration, service, context, callerAnchor, callerEntry, deferCachedWait, out pending, out faultedEntry);
    }

    private object? ResolveKeyed(
        ServiceKey key,
        ServiceResolutionContext context,
        OwnedServiceScope callerAnchor,
        ServiceCacheEntry? callerEntry,
        bool deferCachedWait,
        out Task<ServiceOutcome>? pending,
        out ServiceCacheEntry? faultedEntry
    ) {
        pending = null;
        faultedEntry = null;
        if (!_keyedRegistrations.TryGetValue(key, out ServiceRegistration? registration))
            throw new DependencyInjectionException($"Unregistered keyed service {key}; path: {context.PathText}.");

        return ResolveRegistration(registration, key.ServiceType, context, callerAnchor, callerEntry, deferCachedWait, out pending, out faultedEntry);
    }

    private object? ResolveRegistration(
        IServiceRegistration registration,
        Type service,
        ServiceResolutionContext context,
        OwnedServiceScope callerAnchor,
        ServiceCacheEntry? callerEntry,
        bool deferCachedWait,
        out Task<ServiceOutcome>? pending,
        out ServiceCacheEntry? faultedEntry
    ) {
        pending = null;
        faultedEntry = null;
        if (context.Path.Contains(registration)) throw registration.Error($"Dependency cycle: {context.PathText} -> {registration.Label}.");

        Type? scopeType = registration.Record.Lifetime.ScopeType;
        OwnedServiceScope anchor = scopeType is null ? callerAnchor : FindServiceOwner(callerAnchor, scopeType, registration);
        if (registration.Instance is {} instance) return instance;
        if (scopeType is null) return Activate(registration, anchor, callerEntry, context, false);

        ServiceCacheEntry entry;
        bool construct;
        lock (Gate) {
            ConcurrentDictionary<IServiceRegistration, ServiceCacheEntry> cache = anchor.GetOrCreateRegistrationCache();
            construct = !cache.TryGetValue(registration, out entry!);
            if (construct) {
                entry = new ServiceCacheEntry(registration.Label);
                cache.TryAdd(registration, entry);
                if (ReferenceEquals(_registrations.GetValueOrDefault(service), registration))
                    anchor.GetOrCreateCache().TryAdd(service, entry);
            }

            if (callerEntry is {} parent && !entry.IsCompleted) {
                if (Reaches(entry, parent, [])) throw registration.Error($"Concurrent dependency cycle between {parent.Label} and {entry.Label}.");

                parent.AddDependency(entry);
            }
        }

        object? completedValue = null;
        try {
            if (construct) {
                ServiceOutcome outcome;
                try { outcome = Activate(registration, anchor, entry, context, true); }
                catch (Exception exception) {
                    if (deferCachedWait) {
                        faultedEntry = entry;
                        throw;
                    }

                    outcome = new ServiceFailure(exception);
                }

                entry.SetOutcome(outcome);
                if (outcome.Value is not ServiceFailure) completedValue = outcome.Value;
            }

            if (entry.TryGetPublishedValue(out object publishedValue)) {
                return publishedValue;
            }

            if (!construct && deferCachedWait) {
                Emit(ServiceDiagnosticEventKind.CacheWait, service, callerAnchor, registration.Record.Lifetime.ScopeType, null, context, null, 0, 0);
                pending = entry.GetCompletionTask();
                return null;
            }

            // Nested dependencies are requested from synchronous constructors and factories, so they cannot suspend.
            // Top-level ResolveAsync callers defer this wait and await the entry in AwaitCachedResolutionAsync instead.
            ServiceOutcome completed = entry.TryGetOutcome(out ServiceOutcome completedOutcome)
                ? completedOutcome
                : entry.GetCompletionTask().GetAwaiter().GetResult();
            return GetServiceValue(completed);
        }
        finally {
            lock (Gate) {
                callerEntry?.Dependencies.Remove(entry);
                if (construct && completedValue is not null) entry.Publish(completedValue);
            }
        }
    }

    internal T[] ResolveGeneratedCollection<T>(ServiceResolutionContext context, OwnedServiceScope anchor, ServiceCacheEntry? callerEntry) {
        ServiceRegistration[] registrations = _registrationSets.GetValueOrDefault(typeof(T)) ?? Array.Empty<ServiceRegistration>();
        var values = new T[registrations.Length];
        for (int index = 0; index < registrations.Length; index++) {
            values[index] = (T)ResolveRegistration(registrations[index], typeof(T), context, anchor, callerEntry, false, out _, out _)!;
        }

        return values;
    }

    internal T[] ResolveGeneratedKeyedCollection<T, TKey>(TKey key, ServiceResolutionContext context, OwnedServiceScope anchor, ServiceCacheEntry? callerEntry) {
        ServiceKey serviceKey = ServiceKey.Of<T, TKey>(key);
        ServiceRegistration[] registrations = _keyedRegistrationSets.GetValueOrDefault(serviceKey) ?? Array.Empty<ServiceRegistration>();
        var values = new T[registrations.Length];
        for (int index = 0; index < registrations.Length; index++) {
            values[index] = (T)ResolveRegistration(registrations[index], typeof(T), context, anchor, callerEntry, false, out _, out _)!;
        }

        return values;
    }

    private object ResolveGeneratedCollectionObject(Type element, ServiceResolutionContext context, OwnedServiceScope anchor, ServiceCacheEntry? callerEntry) {
        if (!_collectionResolvers.TryGetValue(element, out GeneratedServiceCollectionResolver? resolver))
            throw new DependencyInjectionException($"No generated collection resolver for IEnumerable<{element}>. Add a generated collection dependency or register one explicitly.");

        var generatedResolver = new GeneratedServiceResolver(this, context, anchor, callerEntry);
        return resolver(ref generatedResolver);
    }

    private object Activate(
        IServiceRegistration registration,
        OwnedServiceScope anchor,
        ServiceCacheEntry? cacheEntry,
        ServiceResolutionContext context,
        bool cached
    ) {
        int resourceStart = context.ResourceCount;
        context.AddPath(registration);
        ServiceDiagnosticsOptions? diagnostics = _diagnostics;
        long started = 0;
        long allocated = 0;
        ServiceDiagnosticActivationSource source = default;
        if (diagnostics is not null) {
            started = Stopwatch.GetTimestamp();
            if (diagnostics.MeasureAllocations) allocated = GC.GetAllocatedBytesForCurrentThread();
            source = registration.Activator?.GeneratedCreate is not null
                ? ServiceDiagnosticActivationSource.Generated
                : registration.Factory is not null
                    ? ServiceDiagnosticActivationSource.Factory
                    : ServiceDiagnosticActivationSource.Activator;
        }

        Emit(ServiceDiagnosticEventKind.ActivationStarted, registration.Record.Service, anchor, registration.Record.Lifetime.ScopeType, source, context, null, started, allocated);
        FactoryResolver? resolver = null;
        ServiceProvider? previousProvider = _activatingProvider;
        int previousDepth = _activationDepth;
        if (previousProvider == this) _activationDepth++;
        else {
            _activatingProvider = this;
            _activationDepth = 1;
        }

        try {
            object value;
            if (registration.Inner is {} inner) {
                object innerValue = inner.Instance ?? ResolveRegistration(inner, inner.Record.Service, context, anchor, cacheEntry, false, out _, out _)!;
                if (registration.Activator?.GeneratedCreate is {} generated) {
                    var generatedResolver = new GeneratedServiceResolver(this, context, anchor, cacheEntry, inner);
                    value = generated(ref generatedResolver);
                }
                else value = registration.DecoratorFactory!(innerValue);
            }
            else if (registration.Activator?.GeneratedCreate is {} generated) {
                var generatedResolver = new GeneratedServiceResolver(this, context, anchor, cacheEntry);
                value = generated(ref generatedResolver);
            }
            else {
                resolver = new FactoryResolver(this, context, anchor, cacheEntry);
                value = (registration.Factory ?? registration.Activator!.Create!)(resolver);
            }

            if (value is null || !registration.Record.Service.IsInstanceOfType(value)) throw registration.Error("Factory returned null or an incompatible object.");

            if (IsDisposable(value)) {
                lock (Gate) {
                    if (_external.ContainsKey(value) || !_claimed.TryAdd(value, ++_constructionOrder))
                        throw registration.Error("Factory returned an object already owned or registered externally. Return a new instance.");
                }

                context.AddResource(value);
            }

            if (cached) context.CommitResources(anchor, resourceStart);
            Emit(ServiceDiagnosticEventKind.ActivationCompleted, registration.Record.Service, anchor, registration.Record.Lifetime.ScopeType, source, context, null, started, allocated);
            return value;
        }
        catch (Exception exception) {
            context.FailResources(resourceStart);
            Emit(ServiceDiagnosticEventKind.ActivationFailed, registration.Record.Service, anchor, registration.Record.Lifetime.ScopeType, source, context, exception, started, allocated);
            if (exception is DependencyInjectionException) throw;

            throw registration.Error("Activation failed.", exception);
        }
        finally {
            resolver?.Close();
            context.RemovePath(registration);
            _activatingProvider = previousProvider;
            _activationDepth = previousDepth;
        }
    }

    private ServiceResolutionContext RentContext() {
        ServiceResolutionContext? context = _spareContexts;
        if (context is null) return new ServiceResolutionContext(this);

        _spareContexts = context.Next;
        context.Reset(this);
        return context;
    }

    private static void ReturnContext(ServiceResolutionContext context) {
        context.Reset(null);
        context.Next = _spareContexts;
        _spareContexts = context;
    }

    private async ValueTask<object> CompleteFailedResolutionAsync(
        OwnedServiceScope scope,
        List<object> failed,
        Exception error,
        ServiceCacheEntry? faultedEntry
    ) {
        try {
            List<Exception> failures = await CleanupAsync(failed).ConfigureAwait(false);
            if (failures.Count != 0) {
                failures.Insert(0, error);
                error = new AggregateException("Activation cleanup failed.", failures);
            }

            faultedEntry?.SetOutcome(new ServiceFailure(error));
            ExceptionDispatchInfo.Capture(error).Throw();
            return null!;
        }
        finally { scope.Exit(); }
    }

    private static async ValueTask<object> AwaitCachedResolutionAsync(OwnedServiceScope scope, Task<ServiceOutcome> pending) {
        try {
            return GetServiceValue(await pending.ConfigureAwait(false));
        }
        finally { scope.Exit(); }
    }

    private bool TryResolveWithoutActivation(OwnedServiceScope scope, Type service, out object value) {
        if (IsProviderService(service)) {
            value = ResolveProviderService(scope, service);
            return true;
        }

        if (InputOwners.TryGetValue(service, out Type? inputOwner)) {
            value = FindInputOwner(scope, inputOwner, service).Inputs![service];
            return true;
        }

        if (!_registrations.TryGetValue(service, out ServiceRegistration? registration)) {
            value = null!;
            return false;
        }

        if (registration.Instance is {} instance) {
            value = instance;
            return true;
        }

        if (registration.Record.Lifetime.ScopeType is not {} scopeType) {
            value = null!;
            return false;
        }

        OwnedServiceScope anchor = FindServiceOwner(scope, scopeType, registration);
        ServiceCacheEntry? entry;
        lock (Gate) {
            anchor.TryGetCacheEntry(registration, out entry);
        }

        if (entry is null) {
            value = null!;
            return false;
        }

        if (entry.TryGetPublishedValue(out value)) return true;

        if (!entry.IsCompleted) {
            value = null!;
            return false;
        }

        entry.TryGetOutcome(out ServiceOutcome outcome);
        value = GetServiceValue(outcome);
        return true;
    }

    internal bool TryResolveKeyedWithoutActivation(OwnedServiceScope scope, ServiceKey key, out object value) {
        if (!_keyedRegistrations.TryGetValue(key, out ServiceRegistration? registration)) {
            value = null!;
            return false;
        }

        if (registration.Instance is {} instance) {
            value = instance;
            return true;
        }

        if (registration.Record.Lifetime.ScopeType is not {} scopeType) {
            value = null!;
            return false;
        }

        OwnedServiceScope anchor = FindServiceOwner(scope, scopeType, registration);
        ServiceCacheEntry? entry;
        lock (Gate) {
            anchor.TryGetCacheEntry(registration, out entry);
        }

        if (entry is null) {
            value = null!;
            return false;
        }

        if (entry.TryGetPublishedValue(out value)) return true;

        if (!entry.IsCompleted) {
            value = null!;
            return false;
        }

        entry.TryGetOutcome(out ServiceOutcome outcome);
        value = GetServiceValue(outcome);
        return true;
    }

    private static object GetServiceValue(ServiceOutcome outcome) {
        if (outcome.Value is ServiceFailure failure) ExceptionDispatchInfo.Capture(failure.Error).Throw();
        return outcome.Value ?? throw new InvalidOperationException("Unexpected null service outcome.");
    }

    private static OwnedServiceScope? FindOwner(OwnedServiceScope from, Type type) {
        for (OwnedServiceScope? scope = from; scope is not null; scope = scope.Parent) {
            if (scope.ScopeType == type) return scope;
        }

        return null;
    }

    private static OwnedServiceScope FindInputOwner(OwnedServiceScope from, Type type, Type input)
        => FindOwner(from, type) ?? throw new DependencyInjectionException(
            $"Missing ownership scope {type.Name} for input {input}, resolving from {from.ScopeType.Name}. Descendants and siblings are not visible."
        );

    private static OwnedServiceScope FindServiceOwner(OwnedServiceScope from, Type type, IServiceRegistration registration)
        => FindOwner(from, type) ?? throw new DependencyInjectionException(
            $"Missing ownership scope {type.Name} for {registration.Label}, resolving from {from.ScopeType.Name}. Descendants and siblings are not visible."
        );

    private object ResolveProviderService(OwnedServiceScope from, Type service) {
        if (FindOwner(from, typeof(AterraHost)) is null)
            throw new DependencyInjectionException(
                $"Missing ownership scope {nameof(AterraHost)} for provider service {service}, resolving from {from.ScopeType.Name}.");

        return this;
    }

    private static bool Reaches(ServiceCacheEntry from, ServiceCacheEntry target, HashSet<ServiceCacheEntry> visited) =>
        from == target || visited.Add(from) && from.Dependencies is {} dependencies && dependencies.Any(next => Reaches(next, target, visited));

    private static bool IsDisposable(object instance) => instance is IDisposable or IAsyncDisposable;
    internal static bool IsProviderService(Type service) => service == typeof(IServiceProvider) || service == typeof(ServiceProvider);

    internal async Task<List<Exception>> CleanupAsync(List<object> instances) {
        long started = _diagnostics is null ? 0 : Stopwatch.GetTimestamp();
        List<Exception>? errors = null;
        // Nested failures and concurrent activations can reach this list in a different order
        // from successful construction. The ownership claim is the linearization point.
        lock (Gate) {
            instances.Sort((left, right) => _claimed[left].CompareTo(_claimed[right]));
        }

        for (int index = instances.Count - 1; index >= 0; index--) {
            object instance = instances[index];
            try {
                if (instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (instance is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception exception) { (errors ??= []).Add(exception); }
            finally {
                lock (Gate) {
                    _claimed.Remove(instance);
                }
            }
        }

        instances.Clear();
        Emit(ServiceDiagnosticEventKind.CleanupCompleted, null, null, null, null, null,
            _diagnostics is null || errors is null ? null : new AggregateException(errors), started, 0);
        return errors ?? EmptyCleanupErrors;
    }

    internal List<Exception> CleanupEmpty() {
        Emit(ServiceDiagnosticEventKind.CleanupCompleted, null, null, null, null, null, null, 0, 0);
        return EmptyCleanupErrors;
    }

    internal void EmitScope(ServiceDiagnosticEventKind kind, OwnedServiceScope scope)
        => Emit(kind, null, scope, null, null, null, null, 0, 0);

    private void Emit(
        ServiceDiagnosticEventKind kind,
        Type? service,
        OwnedServiceScope? scope,
        Type? lifetime,
        ServiceDiagnosticActivationSource? source,
        ServiceResolutionContext? context,
        Exception? error,
        long started,
        long allocated
    ) {
        ServiceDiagnosticsOptions? diagnostics = _diagnostics;
        if (diagnostics is null) return;

        var snapshot = new ServiceDiagnosticEvent {
            Kind = kind,
            Sequence = Interlocked.Increment(ref _diagnosticSequence),
            ServiceType = service,
            KeyType = context is null ? null : context.Path.LastOrDefault()?.Key?.KeyType,
            Key = context is null ? null : context.Path.LastOrDefault()?.Key?.Value,
            ScopeType = scope?.ScopeType,
            LifetimeScopeType = lifetime,
            ActivationSource = source,
            ResolutionPath = context is null
                ? ImmutableArray<string>.Empty
                : context.Path.Select(registration => registration.Label).ToImmutableArray(),
            Duration = started == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(started),
            AllocatedBytes = diagnostics.MeasureAllocations && started != 0
                ? GC.GetAllocatedBytesForCurrentThread() - allocated
                : null,
            Error = error
        };

        try { diagnostics.Sink.Write(snapshot); }
        catch {
            /* Diagnostics must never change resolution or cleanup behavior. */
        }
    }

    internal List<Exception> Cleanup(List<object> instances) {
        long started = _diagnostics is null ? 0 : Stopwatch.GetTimestamp();
        List<Exception>? errors = null;
        lock (Gate) {
            instances.Sort((left, right) => _claimed[left].CompareTo(_claimed[right]));
        }

        for (int index = instances.Count - 1; index >= 0; index--) {
            object instance = instances[index];
            try {
                if (instance is IDisposable disposable) disposable.Dispose();
                else
                    (errors ??= []).Add(new InvalidOperationException(
                        $"Cannot synchronously dispose async-only resource {instance.GetType()}."));
            }
            catch (Exception exception) { (errors ??= []).Add(exception); }
            finally {
                lock (Gate) {
                    _claimed.Remove(instance);
                }
            }
        }

        instances.Clear();
        Emit(ServiceDiagnosticEventKind.CleanupCompleted, null, null, null, null, null,
            _diagnostics is null || errors is null ? null : new AggregateException(errors), started, 0);
        return errors ?? EmptyCleanupErrors;
    }

    internal void ReleaseProvider() {
        _claimed.Clear();
        _external.Clear();
        _registrations.Clear();
        _keyedRegistrations.Clear();
    }

    internal void RejectReentrantResolution() {
        if (_activatingProvider == this && _activationDepth != 0)
            throw new DependencyInjectionException("Reentrant public resolution during activation is prohibited; factories must use their supplied IServiceResolver to preserve cycle and ownership checks.");
    }

    internal void TrackInputs(IReadOnlyCollection<object> inputs) {
        foreach (object input in inputs) {
            if (_claimed.ContainsKey(input)) throw new DependencyInjectionException("A container-owned service cannot also be a caller-owned scope input.");
        }

        foreach (object input in inputs) _external[input] = _external.GetValueOrDefault(input) + 1;
    }

    internal void ReleaseInputs(IEnumerable<object> inputs) {
        foreach (object input in inputs) {
            if (--_external[input] == 0) _external.Remove(input);
        }
    }
}
