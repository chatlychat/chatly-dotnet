using System;
using System.Collections.Generic;

namespace Chatly.Tests;

/// <summary>Raised when a check fails. Both runners surface it as a failure.</summary>
public sealed class CheckFailedException : Exception
{
    public CheckFailedException(string message) : base(message)
    {
    }
}

/// <summary>
/// Assertions, written here rather than taken from xunit.
/// </summary>
/// <remarks>
/// The whole check suite is compiled into two runners — the xunit project and
/// the dependency-free <c>Verify</c> console — so that a machine with no NuGet
/// access can still run the identical assertions. Depending on xunit's
/// <c>Assert</c> would make that impossible, and duplicating the checks would
/// let the two copies drift.
/// </remarks>
public static class Expect
{
    public static void True(bool condition, string what)
    {
        if (!condition)
        {
            throw new CheckFailedException($"expected {what}");
        }
    }

    public static void False(bool condition, string what) => True(!condition, what);

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new CheckFailedException($"{what}: expected <{expected}>, got <{actual}>");
        }
    }

    public static void NotEqual<T>(T unexpected, T actual, string what)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            throw new CheckFailedException($"{what}: expected anything but <{unexpected}>");
        }
    }

    public static void NotNull(object? value, string what)
    {
        if (value is null)
        {
            throw new CheckFailedException($"{what}: expected a value, got null");
        }
    }

    public static void Contains(string haystack, string needle, string what)
    {
        if (haystack is null || !haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new CheckFailedException($"{what}: expected <{haystack}> to contain <{needle}>");
        }
    }

    /// <summary>Run an action and return the exception it threw.</summary>
    public static TException Throws<TException>(Action action, string what)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new CheckFailedException(
                $"{what}: expected {typeof(TException).Name}, got {other.GetType().Name}: {other.Message}");
        }

        throw new CheckFailedException($"{what}: expected {typeof(TException).Name}, nothing was thrown");
    }

    /// <summary>Await a task and return the exception it threw.</summary>
    public static async System.Threading.Tasks.Task<TException> ThrowsAsync<TException>(
        Func<System.Threading.Tasks.Task> action,
        string what)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new CheckFailedException(
                $"{what}: expected {typeof(TException).Name}, got {other.GetType().Name}: {other.Message}");
        }

        throw new CheckFailedException($"{what}: expected {typeof(TException).Name}, nothing was thrown");
    }
}
