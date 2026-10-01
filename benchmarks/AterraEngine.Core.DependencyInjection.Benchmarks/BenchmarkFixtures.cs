// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------

using JetBrains.Annotations;
// ReSharper disable once RedundantUsingDirective
using AterraEngine.Core.DependencyInjection;

namespace AterraEngine.Core.DependencyInjection.Benchmarks;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
[SingletonService<SingletonService>]
public sealed class SingletonService;

[TransientService<TransientService>]
public sealed class TransientService;

[WorldService<ScopedService>]
public sealed class ScopedService;

[WorldService<DisposableTransient>]
public sealed class DisposableTransient : IDisposable {
    public void Dispose() {}
}

[TransientService<Chain1>]
public sealed class Chain1(Chain2 next) {
    public Chain2 Next { get; } = next;
}

[TransientService<Chain2>]
public sealed class Chain2(Chain3 next) {
    public Chain3 Next { get; } = next;
}

[TransientService<Chain3>]
public sealed class Chain3(Chain4 next) {
    public Chain4 Next { get; } = next;
}

[TransientService<Chain4>]
public sealed class Chain4(Chain5 next) {
    public Chain5 Next { get; } = next;
}

[TransientService<Chain5>]
public sealed class Chain5(Chain6 next) {
    public Chain6 Next { get; } = next;
}

[TransientService<Chain6>]
public sealed class Chain6(Chain7 next) {
    public Chain7 Next { get; } = next;
}

[TransientService<Chain7>]
public sealed class Chain7(Chain8 next) {
    public Chain8 Next { get; } = next;
}

[TransientService<Chain8>]
public sealed class Chain8(TransientService leaf) {
    public TransientService Leaf { get; } = leaf;
}

[TransientService<GraphLeaf>]
public sealed class GraphLeaf;

[TransientService<LeftBranch>]
public sealed class LeftBranch(GraphLeaf leaf) {
    public GraphLeaf Leaf { get; } = leaf;
}

[TransientService<RightBranch>]
public sealed class RightBranch(GraphLeaf leaf) {
    public GraphLeaf Leaf { get; } = leaf;
}

[TransientService<RequestHandler>]
public sealed class RequestHandler(SingletonService singleton, ScopedService scoped, LeftBranch left, RightBranch right) {
    public SingletonService Singleton { get; } = singleton;
    public ScopedService Scoped { get; } = scoped;
    public LeftBranch Left { get; } = left;
    public RightBranch Right { get; } = right;
}

[TransientService<CollectionItem>]
public sealed class CollectionItem : ICollectionItem;

public interface ICollectionItem;

[TransientService<KeyedSingleton>]
public sealed class KeyedSingleton;

[TransientService<KeyedTransient>]
public sealed class KeyedTransient;

[TransientService<KeyedCollectionItem>]
public sealed class KeyedCollectionItem : IKeyedItem;

public interface IKeyedItem;

public interface IDecoratedService;

[TransientService<DecoratedService>]
public sealed class DecoratedService : IDecoratedService;

[TransientService<DecoratorOne>]
public sealed class DecoratorOne([DecoratedDependency<IDecoratedService>] IDecoratedService inner) : IDecoratedService {
    [UsedImplicitly]
    public IDecoratedService Inner { get; } = inner;
}

[TransientService<DecoratorTwo>]
public sealed class DecoratorTwo([DecoratedDependency<IDecoratedService>] IDecoratedService inner) : IDecoratedService {
    [UsedImplicitly]
    public IDecoratedService Inner { get; } = inner;
}

[TransientService<DecoratorThree>]
public sealed class DecoratorThree([DecoratedDependency<IDecoratedService>] IDecoratedService inner) : IDecoratedService {
    [UsedImplicitly]
    public IDecoratedService Inner { get; } = inner;
}

// ReSharper disable once UnusedTypeParameter
public interface IClosedBenchmark<T>;

[GeneratedServiceClosure<IClosedBenchmark<string>, ClosedBenchmark<string>>(ServiceScope.Host)]
public sealed class ClosedBenchmark<T> : IClosedBenchmark<T> where T : class {
    // ReSharper disable once UnusedParameter.Local
    public ClosedBenchmark(IEnumerable<T> values) {}
}

[WorldService<SyncDisposable>]
public sealed class SyncDisposable : IDisposable {
    public void Dispose() {}
}

[WorldService<AsyncDisposable>]
public sealed class AsyncDisposable : IAsyncDisposable {
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[WorldService<DualDisposable>]
public sealed class DualDisposable : IDisposable, IAsyncDisposable {
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() {}
}

[TransientService<DiagnosticService>]
public sealed class DiagnosticService;

public sealed class NoOpDiagnosticSink : IServiceDiagnosticSink {
    public static readonly NoOpDiagnosticSink Instance = new();
    private NoOpDiagnosticSink() {}
    public void Write(ServiceDiagnosticEvent diagnosticEvent) {}
}
