// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal static class ServiceCollectionValidator {
    public static void Validate(
        IReadOnlyDictionary<Type, ServiceActivationPlan> activators,
        IReadOnlyDictionary<Type, Type> inputs,
        IReadOnlyDictionary<Type, Type[]> parents,
        IReadOnlyDictionary<Type, ServiceRegistration> registrations
    ) {
        Validate(activators, inputs, parents, registrations,
            registrations.ToDictionary(keySelector: pair => pair.Key, elementSelector: pair => new List<ServiceRegistration> { pair.Value }));
    }

    public static void Validate(
        IReadOnlyDictionary<Type, ServiceActivationPlan> activators,
        IReadOnlyDictionary<Type, Type> inputs,
        IReadOnlyDictionary<Type, Type[]> parents,
        IReadOnlyDictionary<Type, ServiceRegistration> registrations,
        IReadOnlyDictionary<Type, List<ServiceRegistration>> registrationSets,
        IEnumerable<ServiceRegistration>? keyedRegistrations = null,
        IEnumerable<ServiceRegistration>? keyedRegistrationSets = null
    ) {
        Dictionary<Type, HashSet<Type>> guaranteedAncestors = ValidateScopes(parents);
        ValidateInputs(inputs, parents);
        List<ServiceRegistration> keyed = CollectDistinct(keyedRegistrations, keyedRegistrationSets);
        ValidateRegistrations(activators, parents, registrationSets, keyed);
        ValidateDependencies(inputs, registrations, registrationSets, guaranteedAncestors);
    }

    private static List<ServiceRegistration> CollectDistinct(
        IEnumerable<ServiceRegistration>? first,
        IEnumerable<ServiceRegistration>? second
    ) {
        IEnumerable<ServiceRegistration> serviceRegistrationsFirst = first as ServiceRegistration[] ?? first?.ToArray() ?? [];
        IEnumerable<ServiceRegistration> serviceRegistrationsSecond = second as ServiceRegistration[] ?? second?.ToArray() ?? [];
        int capacity = Count(serviceRegistrationsFirst) + Count(serviceRegistrationsSecond);
        var result = new List<ServiceRegistration>(capacity);
        var seen = new HashSet<ServiceRegistration>(capacity);
        Add(serviceRegistrationsFirst);
        Add(serviceRegistrationsSecond);
        return result;

        void Add(IEnumerable<ServiceRegistration>? registrations) {
            if (registrations is null) return;

            result.AddRange(registrations.Where(seen.Add));
        }

        static int Count(IEnumerable<ServiceRegistration>? registrations) {
            return registrations switch {
                ICollection<ServiceRegistration> collection => collection.Count,
                IReadOnlyCollection<ServiceRegistration> collection => collection.Count,
                _ => 0
            };
        }
    }

    private static Dictionary<Type, HashSet<Type>> ValidateScopes(IReadOnlyDictionary<Type, Type[]> parentsByScope) {
        var guaranteedAncestors = new Dictionary<Type, HashSet<Type>>();
        var visiting = new HashSet<Type>();

        foreach (Type root in parentsByScope.Keys) {
            if (guaranteedAncestors.ContainsKey(root)) continue;

            var stack = new Stack<ScopeFrame>();
            Push(root);

            while (stack.TryPeek(out ScopeFrame? frame)) {
                if (frame.NextParent < frame.Parents.Length) {
                    Type parent = frame.Parents[frame.NextParent];
                    if (guaranteedAncestors.TryGetValue(parent, out HashSet<Type>? ancestors)) {
                        frame.AddParent(ancestors);
                        frame.NextParent++;
                    }
                    else {
                        Push(parent);
                    }

                    continue;
                }

                HashSet<Type> result = frame.CommonAncestors ?? [];
                result.Add(frame.Scope);
                visiting.Remove(frame.Scope);
                guaranteedAncestors.Add(frame.Scope, result);
                stack.Pop();
            }

            continue;

            void Push(Type scope) {
                if (!parentsByScope.TryGetValue(scope, out Type[]? scopeParents))
                    throw new DependencyInjectionException($"Undeclared scope {scope}.");
                if (!visiting.Add(scope)) throw new DependencyInjectionException($"Scope parent cycle involving {scope}.");
                if (scope.ContainsGenericParameters || scope == typeof(void) || scope != typeof(AterraSingleton) && scopeParents.Length == 0)
                    throw new DependencyInjectionException(
                        $"Invalid scope {scope}: it must have a path to {nameof(AterraSingleton)}.");

                stack.Push(new ScopeFrame(scope, scopeParents));
            }
        }

        return guaranteedAncestors;
    }

    private static void ValidateInputs(
        IReadOnlyDictionary<Type, Type> inputs,
        IReadOnlyDictionary<Type, Type[]> parents
    ) {
        foreach ((Type input, Type scope) in inputs) {
            if (!parents.ContainsKey(scope) || input.ContainsGenericParameters || input == typeof(void) || ServiceProvider.IsProviderService(input))
                throw new DependencyInjectionException($"Invalid input declaration {input} for {scope}.");
        }
    }

    private static void ValidateRegistrations(
        IReadOnlyDictionary<Type, ServiceActivationPlan> activators,
        IReadOnlyDictionary<Type, Type[]> parents,
        IReadOnlyDictionary<Type, List<ServiceRegistration>> registrationSets,
        IEnumerable<ServiceRegistration> keyedRegistrations = null!
    ) {
        var externalObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (ServiceRegistration registration in registrationSets.Values.SelectMany(static registrations => registrations).Concat(keyedRegistrations)) {
            (ServiceLifetime lifetime, Type service, Type implementation, _) = registration.Record;
            // A decorator retains its original registration. Validate that retained registration as well, because
            // wrapping replaces it in the active registration set before Build selects generated activators.
            EnsureInnerActivators(registration.Inner, activators);
            if (ServiceProvider.IsProviderService(service))
                throw registration.Error("Conflicts with the built-in provider service.");
            if (service.ContainsGenericParameters || service == typeof(void) || service.IsByRef || service.IsPointer)
                throw registration.Error("Service must be a closed, resolvable type.");
            if (lifetime.ScopeType is {} scope && !parents.ContainsKey(scope))
                throw registration.Error($"Undeclared lifetime scope {scope}.");

            if (registration.Instance is {} instance) {
                if (!externalObjects.Add(instance)) throw registration.Error("The same external object cannot be registered twice.");

                continue;
            }

            if (registration.Factory is not null) continue;

            // A decorator owns its inner registration and either has an explicit factory or a preselected
            // generated constructor plan. Its implementation is validated when the wrapper is created.
            if (registration.DecoratorFactory is not null) continue;

            if (!implementation.IsClass || implementation.IsAbstract || implementation.ContainsGenericParameters || !service.IsAssignableFrom(implementation))
                throw registration.Error($"Invalid implementation {implementation}.");

            if (registration.Activator is not null) continue;

            if (!activators.TryGetValue(implementation, out ServiceActivationPlan? activator))
                throw registration.Error($"No generated activator for {implementation}. Call RegisterActivators for its assembly or register an explicit factory.");

            registration.Activator = activator;
        }

        static void EnsureInnerActivators(
            ServiceRegistration? registration,
            IReadOnlyDictionary<Type, ServiceActivationPlan> activators
        ) {
            if (registration is null) return;

            EnsureInnerActivators(registration.Inner, activators);
            if (registration.Activator is not null || registration.Factory is not null ||
                registration.Instance is not null || registration.DecoratorFactory is not null) return;

            if (!activators.TryGetValue(registration.Record.Implementation, out ServiceActivationPlan? activator))
                throw registration.Error($"No generated activator for {registration.Record.Implementation}. Call RegisterActivators for its assembly or register an explicit factory.");

            registration.Activator = activator;
        }
    }

    private static void ValidateDependencies(
        IReadOnlyDictionary<Type, Type> inputs,
        IReadOnlyDictionary<Type, ServiceRegistration> registrations,
        IReadOnlyDictionary<Type, List<ServiceRegistration>> registrationSets,
        IReadOnlyDictionary<Type, HashSet<Type>> guaranteedAncestors
    ) {
        var validated = new HashSet<(ServiceRegistration Registration, Type? Anchor)>();

        foreach (ServiceRegistration rootRegistration in registrationSets.Values.SelectMany(static registrations => registrations)) {
            var path = new List<Type>();
            var stack = new Stack<DependencyFrame>();
            PushRegistration(rootRegistration, null);

            while (stack.TryPeek(out DependencyFrame? frame)) {
                if (frame.NextDependency < frame.Dependencies.Length) {
                    Type dependency = frame.Dependencies[frame.NextDependency++];
                    Push(dependency, frame.ChildAnchor);
                    continue;
                }

                path.RemoveAt(path.Count - 1);
                validated.Add((frame.Registration, frame.Anchor));
                stack.Pop();
            }

            continue;

            void Push(Type service, Type? anchor) {
                if (ServiceProvider.IsProviderService(service)) {
                    if (anchor is not null && !guaranteedAncestors[anchor].Contains(typeof(AterraHost)))
                        throw new DependencyInjectionException(
                            $"Lifetime violation: {FormatPath(path, registrations)} -> provider service {service} requires {nameof(AterraHost)} from {anchor}.");

                    return;
                }

                if (inputs.TryGetValue(service, out Type? inputScope)) {
                    if (anchor is not null && !guaranteedAncestors[anchor].Contains(inputScope))
                        throw new DependencyInjectionException($"Lifetime violation: {FormatPath(path, registrations)} -> input {service} requires {inputScope} from {anchor}.");

                    return;
                }

                if (service.IsGenericType && service.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
                    Type element = service.GetGenericArguments()[0];
                    if (registrationSets.TryGetValue(element, out List<ServiceRegistration>? elements)) {
                        foreach (ServiceRegistration item in elements) PushRegistration(item, anchor);
                    }

                    return;
                }

                if (!registrations.TryGetValue(service, out ServiceRegistration? registration))
                    throw new DependencyInjectionException($"Missing dependency {service}; path: {FormatPath(path, registrations)}.");
                if (path.Contains(service))
                    throw registration.Error($"Dependency cycle: {string.Join(" -> ", path.Append(service).Select(type => type.Name))}.");

                PushRegistration(registration, anchor);
            }

            void PushRegistration(ServiceRegistration registration, Type? anchor) {
                if (validated.Contains((registration, anchor))) return;

                path.Add(registration.Record.Service);
                Type? owner = registration.Record.Lifetime.ScopeType;
                if (anchor is not null && owner is not null && !guaranteedAncestors[anchor].Contains(owner))
                    throw registration.Error($"Lifetime violation from {anchor}: {FormatPath(path, registrations)} -> {registration.Label} requires {owner}.");

                stack.Push(new DependencyFrame(registration, anchor, owner ?? anchor, registration.Activator?.Dependencies ?? []));
            }
        }
    }

    private static string FormatPath(IEnumerable<Type> path, IReadOnlyDictionary<Type, ServiceRegistration> registrations)
        => string.Join(" -> ", path.Select(type => registrations[type].Label));

    private sealed class ScopeFrame(Type scope, Type[] parents) {
        public HashSet<Type>? CommonAncestors { get; private set; }
        public int NextParent { get; set; }
        public Type[] Parents { get; } = parents;
        public Type Scope { get; } = scope;

        public void AddParent(HashSet<Type> ancestors) {
            if (CommonAncestors is null) CommonAncestors = new HashSet<Type>(ancestors);
            else CommonAncestors.IntersectWith(ancestors);
        }
    }

    private sealed class DependencyFrame(ServiceRegistration registration, Type? anchor, Type? childAnchor, Type[] dependencies) {
        public Type? Anchor { get; } = anchor;
        public Type? ChildAnchor { get; } = childAnchor;
        public Type[] Dependencies { get; } = dependencies;
        public int NextDependency { get; set; }
        public ServiceRegistration Registration { get; } = registration;
    }
}
