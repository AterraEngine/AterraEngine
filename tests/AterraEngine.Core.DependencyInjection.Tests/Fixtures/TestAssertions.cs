// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------

namespace AterraEngine.Core.DependencyInjection.Tests.Fixtures;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
internal static class Check {
    internal static void True(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Same(object expected, object actual) => True(ReferenceEquals(expected, actual), "Expected the same instance.");
    internal static void Different(object first, object second) => True(!ReferenceEquals(first, second), "Expected different instances.");

    internal static T Fails<T>(Action action, string text = "") where T : Exception {
        try { action(); }
        catch (T exception) {
            True(exception.ToString().Contains(text, StringComparison.OrdinalIgnoreCase), $"Expected '{text}' in {exception}.");
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    internal static async Task<T> FailsAsync<T>(Func<Task> action, string text = "") where T : Exception {
        try { await action(); }
        catch (T exception) {
            True(exception.ToString().Contains(text, StringComparison.OrdinalIgnoreCase), $"Expected '{text}' in {exception}.");
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
