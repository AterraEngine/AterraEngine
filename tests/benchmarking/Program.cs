using BenchmarkDotNet.Running;

namespace AterraEngine.Core.DependencyInjection.Benchmarks;

internal static class Program {
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
