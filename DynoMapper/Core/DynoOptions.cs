namespace DynoMapper.Core;

/// <summary>
/// Supported database providers for DynoMapper.
/// </summary>
public enum DbProvider
{
    SqlServer,
    PostgreSQL,
    MySQL,
    EFCore
}

/// <summary>
/// Full configuration for DynoMapper.
/// Set once in Program.cs — never pass connection strings per query again.
/// </summary>
public sealed class DynoOptions
{
    /// <summary>Your database connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Which database provider to use.</summary>
    public DbProvider Provider { get; set; } = DbProvider.SqlServer;

    /// <summary>Default command timeout in seconds. Default: 30.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Retry policy for transient failures (deadlocks, timeouts, connection drops).
    /// Enabled by default — 3 attempts, exponential backoff.
    /// </summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>Log slow queries to console. Default: false.</summary>
    public bool EnableQueryLogging { get; set; } = false;

    /// <summary>Queries slower than this (ms) are flagged. Default: 500ms.</summary>
    public int SlowQueryThresholdMs { get; set; } = 500;
}
