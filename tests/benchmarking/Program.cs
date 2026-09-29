// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Reflection;
using BenchmarkDotNet.Running;

namespace AterraEngine.Core.DependencyInjection.Benchmarks;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal static class Program {
    public static void Main(string[] args) {
        Assembly assembly = typeof(Program).Assembly;
        BenchmarkSwitcher.FromAssembly(assembly).Run(args);
    }
}
