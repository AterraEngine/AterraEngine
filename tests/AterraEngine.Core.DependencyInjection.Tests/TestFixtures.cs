using System.Diagnostics.CodeAnalysis;
// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using JetBrains.Annotations;

namespace AterraEngine.Core.DependencyInjection.Tests;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal static class TestFixtures {
    internal sealed class BuildOnlyService;
    internal sealed class ModuleService;
    internal sealed class ChildScope;
    internal sealed record HostInput([UsedImplicitly] string Name);
    internal sealed class WrongInput;
    internal sealed class UnrelatedService;
    internal sealed class MissingService;

    internal sealed class ContractService;
    internal sealed class SecondContractService;
    internal sealed class ContractInput;

    internal sealed record SingletonInput(string Name);
    internal sealed record WorldInput([UsedImplicitly] string Name);
    internal sealed class SingletonConsumer(SingletonInput input) {
        public SingletonInput Input { get; } = input;
    }
    internal sealed class InputConsumer(WorldInput input) {
        public WorldInput Input { get; } = input;
    }
    internal sealed class LeftScope;
    internal sealed class RightScope;
    internal sealed class JoinedScope;
    internal sealed class JoinedService;
    internal interface IClaimedInput;
    internal sealed class ClaimedObject : IClaimedInput;
    internal sealed class DisposableService(Action onDispose) : IDisposable {
        public void Dispose() => onDispose();
    }

    internal sealed class MutationService;
    internal sealed class MutationScope;
    internal sealed class MutationInput;
    internal sealed class MissingMutationService;
    internal sealed class FirstMutationService(Action dispose) : IDisposable {
        public void Dispose() => dispose();
    }
    internal sealed class SecondMutationService(Action dispose) : IDisposable {
        public void Dispose() => dispose();
    }
}
