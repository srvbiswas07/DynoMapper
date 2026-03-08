using System.Data;
using System.Data.Common;
using DynoMapper.Core;
using DynoMapper.Mapper;

namespace DynoMapper.SqlLayer;

/// <summary>
/// A fully scoped database transaction.
/// Supports ALL query types — raw SQL, stored procedures, multi-result sets.
/// Auto-rolls back if CommitAsync is never called (safe by default).
///
/// <code>
/// await using var tx = await _sql.BeginTransactionAsync();
/// try
/// {
///     var orderId = await tx.QueryScalarAsync&lt;int&gt;(
///         "INSERT INTO Orders OUTPUT INSERTED.Id VALUES (@Total)",
///         new { Total = 500m });
///
///     await tx.ExecuteAsync(
///         "INSERT INTO OrderItems VALUES (@OrderId, @ProductId)",
///         new { OrderId = orderId, ProductId = 3 });
///
///     await tx.ExecuteSpAsync("sp_NotifyWarehouse", new { OrderId = orderId });
///
///     await tx.CommitAsync(); // all or nothing
/// }
/// catch { throw; } // auto-rollback on DisposeAsync
/// </code>
/// </summary>
public interface IDynoTransaction : IAsyncDisposable
{
    // ── Raw SQL ───────────────────────────────────────────────────────
    Task<DynoResult> QueryListAsync(string query, object? parameters = null, CancellationToken ct = default);
    Task<DynoResult> QuerySingleAsync(string query, object? parameters = null, CancellationToken ct = default);
    Task<DynoResult> QueryScalarAsync(string query, object? parameters = null, CancellationToken ct = default);
    Task<DynoResult> ExecuteAsync(string query, object? parameters = null, CancellationToken ct = default);
    Task<DynoResult> ExecuteWithOutputAsync(string query, object? parameters = null, CancellationToken ct = default);

    // ── Stored Procedures ─────────────────────────────────────────────
    Task<DynoResult> QueryListSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default);
    Task<DynoResult> QuerySingleSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default);
    Task<DynoResult> ExecuteSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default);

    // ── Multi Result Set ──────────────────────────────────────────────
    Task<IReadOnlyList<DynoResult>> QueryMultipleAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default);

    // ── Control ───────────────────────────────────────────────────────
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>Create a savepoint — partial rollback within the transaction.</summary>
    Task SavepointAsync(string name, CancellationToken ct = default);

    /// <summary>Rollback to a savepoint without rolling back the whole transaction.</summary>
    Task RollbackToSavepointAsync(string name, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DynoTransaction : IDynoTransaction
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly DynoOptions _options;
    private bool _completed;

    internal DynoTransaction(DbConnection connection, DbTransaction transaction, DynoOptions options)
    {
        _connection = connection;
        _transaction = transaction;
        _options = options;
    }

    // ── Raw SQL ───────────────────────────────────────────────────────

    public Task<DynoResult> QueryListAsync(string query, object? parameters = null, CancellationToken ct = default)
        => RunReaderAsync(query, parameters, CommandType.Text, ct);

    public Task<DynoResult> QuerySingleAsync(string query, object? parameters = null, CancellationToken ct = default)
        => RunReaderAsync(query, parameters, CommandType.Text, ct);

    public async Task<DynoResult> QueryScalarAsync(string query, object? parameters = null, CancellationToken ct = default)
    {
        await using var cmd = Build(query, CommandType.Text, parameters);
        var value = await cmd.ExecuteScalarAsync(ct);
        return DynoResult.FromScalar(value == DBNull.Value ? null : value);
    }

    public async Task<DynoResult> ExecuteAsync(string query, object? parameters = null, CancellationToken ct = default)
    {
        await using var cmd = Build(query, CommandType.Text, parameters);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return DynoResult.FromAffected(affected);
    }

    public async Task<DynoResult> ExecuteWithOutputAsync(string query, object? parameters = null, CancellationToken ct = default)
    {
        await using var cmd = Build(query, CommandType.Text, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = await DynoReader.ReadAllAsync(reader, ct);
        return DynoResult.FromRows(rows);
    }

    // ── Stored Procedures ─────────────────────────────────────────────

    public Task<DynoResult> QueryListSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default)
        => RunReaderAsync(procedureName, parameters, CommandType.StoredProcedure, ct);

    public Task<DynoResult> QuerySingleSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default)
        => RunReaderAsync(procedureName, parameters, CommandType.StoredProcedure, ct);

    public async Task<DynoResult> ExecuteSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default)
    {
        await using var cmd = Build(procedureName, CommandType.StoredProcedure, parameters);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return DynoResult.FromAffected(affected);
    }

    // ── Multi Result Set ──────────────────────────────────────────────

    public async Task<IReadOnlyList<DynoResult>> QueryMultipleAsync(
        string query, object? parameters = null,
        CommandType commandType = CommandType.Text, CancellationToken ct = default)
    {
        await using var cmd = Build(query, commandType, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var allSets = await DynoReader.ReadMultipleAsync(reader, ct);
        var results = new List<DynoResult>(allSets.Count);
        foreach (var rows in allSets)
            results.Add(DynoResult.FromRows(rows));
        return results;
    }

    // ── Control ───────────────────────────────────────────────────────

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _transaction.CommitAsync(ct);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _transaction.RollbackAsync(ct);
        _completed = true;
    }

    public async Task SavepointAsync(string name, CancellationToken ct = default)
        => await _transaction.SaveAsync(name, ct);

    public async Task RollbackToSavepointAsync(string name, CancellationToken ct = default)
        => await _transaction.RollbackAsync(name, ct);

    public async ValueTask DisposeAsync()
    {
        // Auto-rollback if CommitAsync was never called
        if (!_completed)
        {
            try { await _transaction.RollbackAsync(); }
            catch { /* swallow — connection may already be closed */ }
        }

        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Internals ─────────────────────────────────────────────────────

    private async Task<DynoResult> RunReaderAsync(
        string query, object? parameters, CommandType commandType, CancellationToken ct)
    {
        await using var cmd = Build(query, commandType, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = await DynoReader.ReadAllAsync(reader, ct);
        return DynoResult.FromRows(rows);
    }

    private DbCommand Build(string query, CommandType commandType, object? parameters)
        => SqlHelper.BuildCommand(_connection, _transaction, query, commandType, parameters);
}