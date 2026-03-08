using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace DynoMapper.Core;

/// <summary>
/// Built-in retry policy for transient database failures.
/// Handles connection drops, deadlocks, timeouts automatically.
/// No external library (Polly) needed — pure .NET.
///
/// Configured via DynoOptions:
/// <code>
/// options.Retry.MaxAttempts     = 3;
/// options.Retry.DelayMs         = 200;   // base delay, doubles each attempt
/// options.Retry.UseJitter       = true;  // adds random ms to avoid thundering herd
/// </code>
/// </summary>
public sealed class RetryOptions
{
    /// <summary>Total attempts including the first try. Default: 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base delay in ms between retries. Doubles each attempt (exponential backoff). Default: 200ms.</summary>
    public int DelayMs { get; set; } = 200;

    /// <summary>Adds random jitter (0–100ms) to avoid thundering herd. Default: true.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>Whether retry is enabled at all. Set false to disable completely. Default: true.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Internal retry executor — wraps any async database operation with retry logic.
/// </summary>
internal static class RetryPolicy
{
    private static readonly Random _rng = new();

    internal static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        RetryOptions options,
        CancellationToken ct = default)
    {
        if (!options.Enabled)
            return await operation();

        var attempt = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < options.MaxAttempts && IsTransient(ex))
            {
                var delay = CalculateDelay(options, attempt);
                await Task.Delay(delay, ct);
            }
        }
    }

    internal static async Task ExecuteAsync(
        Func<Task> operation,
        RetryOptions options,
        CancellationToken ct = default)
    {
        await ExecuteAsync<bool>(async () =>
        {
            await operation();
            return true;
        }, options, ct);
    }

    // ── Transient detection ───────────────────────────────────────────

    /// <summary>
    /// Detects transient errors worth retrying across all three providers.
    /// Does NOT retry on: syntax errors, constraint violations, auth failures.
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        // SQL Server transient errors
        if (ex is SqlException sqlEx)
        {
            return sqlEx.Number switch
            {
                // Connection / network
                -2    => true,  // Timeout
                20    => true,  // General network error
                64    => true,  // Connection forcibly closed
                233   => true,  // No process on other end of pipe
                10053 => true,  // Transport-level error
                10054 => true,  // Connection reset by peer
                10060 => true,  // Connection timed out
                // Deadlock / resource
                1205  => true,  // Deadlock victim
                1222  => true,  // Lock request timeout
                // Throttling (Azure SQL)
                40197 => true,
                40501 => true,
                40613 => true,
                49918 => true,
                49919 => true,
                49920 => true,
                _     => false
            };
        }

        // PostgreSQL transient errors
        if (ex is NpgsqlException npgsqlEx)
        {
            var msg = npgsqlEx.Message;
            return msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("connection", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("deadlock", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("could not connect", StringComparison.OrdinalIgnoreCase);
        }

        // MySQL transient errors
        if (ex is MySqlException mysqlEx)
        {
            return mysqlEx.Number switch
            {
                1040 => true,  // Too many connections
                1213 => true,  // Deadlock
                2003 => true,  // Can't connect
                2006 => true,  // Server gone away
                2013 => true,  // Lost connection during query
                _    => false
            };
        }

        // Generic timeout / IO
        if (ex is TimeoutException
            || ex is System.IO.IOException
            || ex is TaskCanceledException)
            return true;

        // Unwrap AggregateException
        if (ex is AggregateException agg)
            return agg.InnerExceptions.Any(IsTransient);

        return false;
    }

    private static TimeSpan CalculateDelay(RetryOptions options, int attempt)
    {
        // Exponential backoff: 200ms, 400ms, 800ms ...
        var baseMs = options.DelayMs * Math.Pow(2, attempt - 1);
        var jitter  = options.UseJitter ? _rng.Next(0, 100) : 0;
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }
}
