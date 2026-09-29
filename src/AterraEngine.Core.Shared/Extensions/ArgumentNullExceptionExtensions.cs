// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace System;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public static class ArgumentNullExceptionExtensions {
    extension(ArgumentNullException) {
        public static void ThrowIfContainsAnyNull<T, T1>(T argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null) where T : ICollection<T1> {
            if (argument.Any(c => c is null)) {
                throw new ArgumentException($"{paramName} cannot contains a null value.", paramName);
            }
        }
    }
}
