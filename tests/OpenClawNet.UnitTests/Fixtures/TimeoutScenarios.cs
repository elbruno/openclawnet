using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawNet.UnitTests.Fixtures;

/// <summary>
/// Fixture for testing timeout scenarios with CancellationToken.
/// Provides helper methods to simulate various timing conditions.
/// </summary>
public sealed class TimeoutScenarios
{
    /// <summary>
    /// Creates a CancellationToken that will cancel after the specified milliseconds.
    /// </summary>
    public static CancellationToken WithTimeout(int milliseconds)
    {
        var cts = new CancellationTokenSource(milliseconds);
        return cts.Token;
    }

    /// <summary>
    /// Tests that an operation properly handles OperationCanceledException from timeout.
    /// </summary>
    public static async Task<bool> TryExecuteWithTimeoutAsync(Func<CancellationToken, Task> operation, int timeoutMs)
    {
        try
        {
            var cts = new CancellationTokenSource(timeoutMs);
            await operation(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Tests that an operation properly handles OperationCanceledException and returns a fallback value.
    /// </summary>
    public static async Task<T> TryExecuteWithFallbackAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        T fallbackValue,
        int timeoutMs)
    {
        try
        {
            var cts = new CancellationTokenSource(timeoutMs);
            return await operation(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return fallbackValue;
        }
    }
}
