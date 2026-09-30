// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
namespace AterraEngine.Core.DependencyInjection;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class ServiceResolutionContext {
    private List<object>? _failed;
    private ServiceProvider _provider;
    private List<object>? _resources;

    // -----------------------------------------------------------------------------------------------------------------
    // Constructors
    // -----------------------------------------------------------------------------------------------------------------
    internal ServiceResolutionContext(ServiceProvider provider) {
        _provider = provider;
    }

    internal ServiceResolutionContext? Next { get; set; }
    public List<object> Failed => _failed ??= [];
    public List<IServiceRegistration> Path { get; } = [];
    public int ResourceCount => _resources?.Count ?? 0;
    public string PathText => string.Join(" -> ", Path.Select(r => r.Label));
    // -----------------------------------------------------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------------------------------------------------
    internal void AddPath(IServiceRegistration registration) {
        Path.Add(registration);
    }

    internal void RemovePath(IServiceRegistration registration) {
        Path.RemoveAt(Path.Count - 1);
    }

    internal void AddResource(object resource) => (_resources ??= []).Add(resource);

    internal void CommitResources(OwnedServiceScope owner, int start) {
        if (_resources is null || _resources.Count == start) return;

        lock (_provider.Gate) {
            for (int index = start; index < _resources.Count; index++) {
                owner.AddOwned(_resources[index]);
            }
        }

        _resources.RemoveRange(start, _resources.Count - start);
    }

    internal void FailResources(int start) {
        if (_resources is null || _resources.Count == start) return;

        List<object> failed = Failed;
        for (int index = start; index < _resources.Count; index++) {
            failed.Add(_resources[index]);
        }

        _resources.RemoveRange(start, _resources.Count - start);
    }

    internal List<object> TakeFailed() {
        List<object> failed = _failed!;
        _failed = null;
        return failed;
    }

    internal void Reset(ServiceProvider? provider) {
        _provider = provider!;
        Path.Clear();
        _resources?.Clear();
        _failed?.Clear();
        Next = null;
    }
}
