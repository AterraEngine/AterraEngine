using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using AterraEngine.Core.DependencyInjection.Collection;

namespace AterraEngine.Core.DependencyInjection;

/// <summary>Runtime bridge used by source-generated assembly registration code.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedServiceRegistration {
    private static readonly ConditionalWeakTable<Assembly, Registration> Registrations = new();
    private static readonly Lock Gate = new();

    public static void RegisterAssembly(Assembly assembly, Action<ServiceCollection> register) {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(register);

        lock (Gate) {
            if (Registrations.TryGetValue(assembly, out _))
                throw new InvalidOperationException($"Generated services for assembly '{assembly.FullName}' are already registered.");
            Registrations.Add(assembly, new Registration(register));
        }
    }

    internal static void Apply(Assembly assembly, ServiceCollection services) {
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        Registration? registration;
        lock (Gate) Registrations.TryGetValue(assembly, out registration);

        if (registration is null)
            throw new DependencyInjectionException($"Assembly '{assembly.FullName}' has no generated service registrations. Reference the DI generator and add a service attribute.");

        registration.Callback(services);
    }

    private sealed class Registration(Action<ServiceCollection> callback) {
        internal Action<ServiceCollection> Callback { get; } = callback;
    }
}
