using System.Data;
using System.Data.Common;
using System.Text;
using DynoMapper.Abstractions;
using DynoMapper.Core;
using DynoMapper.Mapper;

namespace DynoMapper.SqlLayer;

/// <summary>
/// DynoMapper's core SQL execution engine.
///
/// ✅ Connection string stored once — never pass it per query
/// ✅ Pure dynamic results — zero model/DTO classes
/// ✅ SQL Server, PostgreSQL, MySQL out of the box
/// ✅ Stored procedures (list, single, execute, multi-result)
/// ✅ Transactions with full SP support + auto-rollback
/// ✅ Bulk insert — List of anonymous objects in one shot
/// ✅ OUTPUT clause capture — get inserted/updated rows back
/// ✅ Pagination — built-in OFFSET/FETCH with metadata
/// ✅ Retry on transient failures — exponential backoff, jitter
/// ✅ CommandType override on any method
/// </summary>
public interface ISqlHelper
{
    // ── List ──────────────────────────────────────────────────────────
    Task<DynoResult> QueryListAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default);
    Task<DynoResult> QueryListSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default);

    // ── Single ────────────────────────────────────────────────────────
    Task<DynoResult> QuerySingleAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default);
    Task<DynoResult> QuerySingleSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default);

    // ── Scalar ────────────────────────────────────────────────────────
    Task<DynoResult> QueryScalarAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default);

    // ── Execute (non-query) ───────────────────────────────────────────
    Task<DynoResult> ExecuteAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default);
    Task<DynoResult> ExecuteSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default);

    // ── OUTPUT clause ─────────────────────────────────────────────────
    /// <summary>
    /// Run INSERT/UPDATE/DELETE with an OUTPUT clause and get the output rows back.
    /// e.g. "INSERT INTO Users (Name) OUTPUT INSERTED.Id, INSERTED.CreatedAt VALUES (@Name)"
    /// Returns DynoResult with .List of the OUTPUT rows — no model class needed.
    /// </summary>
    Task<DynoResult> ExecuteWithOutputAsync(string query, object? parameters = null, CancellationToken ct = default);

    // ── Bulk Insert ───────────────────────────────────────────────────
    /// <summary>
    /// Insert a list of anonymous objects into a table in one batched operation.
    /// Column names are inferred from the first object's properties.
    /// e.g. await _sql.BulkInsertAsync("Users", new[] { new { Name="Ali", Email="ali@x.com" }, ... })
    /// Returns total rows inserted.
    /// </summary>
    Task<DynoResult> BulkInsertAsync(string tableName, IEnumerable<object> rows, CancellationToken ct = default);

    // ── Pagination ────────────────────────────────────────────────────
    /// <summary>
    /// Run a query with automatic OFFSET/FETCH pagination.
    /// The query MUST have an ORDER BY clause.
    /// Returns PagedDynoResult with .Data, .TotalCount, .TotalPages, .HasNextPage etc.
    /// e.g. await _sql.QueryPagedAsync("SELECT * FROM Users ORDER BY Id", pageNumber: 2, pageSize: 20)
    /// </summary>
    Task<PagedDynoResult> QueryPagedAsync(string query, int pageNumber, int pageSize, object? parameters = null, CancellationToken ct = default);

    // ── Multi Result Set ──────────────────────────────────────────────
    /// <summary>
    /// Execute a query or stored procedure returning multiple result sets.
    /// Returns one DynoResult per SELECT in the query/procedure.
    /// </summary>
    Task<IReadOnlyList<DynoResult>> QueryMultipleAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default);

    // ── Transactions ──────────────────────────────────────────────────
    /// <summary>
    /// Begin a scoped transaction. Use 'await using' for automatic rollback on failure.
    /// Supports all query types including stored procedures.
    /// </summary>
    Task<IDynoTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class SqlHelper : ISqlHelper
{
    private readonly IDbConnectionFactory _factory;
    private readonly DynoOptions _options;

    public SqlHelper(IDbConnectionFactory factory, DynoOptions options)
    {
        _factory = factory;
        _options = options;
    }

    // ── List ──────────────────────────────────────────────────────────

    public Task<DynoResult> QueryListAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default)
        => RetryPolicy.ExecuteAsync(() => ExecuteReaderAsync(query, parameters, commandType, ct), _options.Retry, ct);

    public Task<DynoResult> QueryListSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default)
        => RetryPolicy.ExecuteAsync(() => ExecuteReaderAsync(procedureName, parameters, CommandType.StoredProcedure, ct), _options.Retry, ct);

    // ── Single ────────────────────────────────────────────────────────

    public Task<DynoResult> QuerySingleAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default)
        => RetryPolicy.ExecuteAsync(() => ExecuteReaderAsync(query, parameters, commandType, ct), _options.Retry, ct);

    public Task<DynoResult> QuerySingleSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default)
        => RetryPolicy.ExecuteAsync(() => ExecuteReaderAsync(procedureName, parameters, CommandType.StoredProcedure, ct), _options.Retry, ct);

    // ── Scalar ────────────────────────────────────────────────────────

    public Task<DynoResult> QueryScalarAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default)
        => RetryPolicy.ExecuteAsync(async () =>
        {
            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = BuildCommand(conn, null, query, commandType, parameters);
            var value = await cmd.ExecuteScalarAsync(ct);
            return DynoResult.FromScalar(value == DBNull.Value ? null : value);
        }, _options.Retry, ct);

    // ── Execute ───────────────────────────────────────────────────────

    public Task<DynoResult> ExecuteAsync(string query, object? parameters = null, CommandType commandType = CommandType.Text, CancellationToken ct = default)
        => RetryPolicy.ExecuteAsync(() => ExecuteNonQueryAsync(query, parameters, commandType, ct), _options.Retry, ct);

    public Task<DynoResult> ExecuteSpAsync(string procedureName, object? parameters = null, CancellationToken ct = default)
        => RetryPolicy.ExecuteAsync(() => ExecuteNonQueryAsync(procedureName, parameters, CommandType.StoredProcedure, ct), _options.Retry, ct);

    // ── OUTPUT clause ─────────────────────────────────────────────────

    public async Task<DynoResult> ExecuteWithOutputAsync(string query, object? parameters = null, CancellationToken ct = default)
    {
        return await RetryPolicy.ExecuteAsync(async () =>
        {
            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = BuildCommand(conn, null, query, CommandType.Text, parameters);

            // OUTPUT clause returns rows via ExecuteReader, not ExecuteNonQuery
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = await DynoReader.ReadAllAsync(reader, ct);
            return DynoResult.FromRows(rows);
        }, _options.Retry, ct);
    }

    // ── Bulk Insert ───────────────────────────────────────────────────

    public async Task<DynoResult> BulkInsertAsync(string tableName, IEnumerable<object> rows, CancellationToken ct = default)
    {
        var rowList = rows.ToList();
        if (rowList.Count == 0)
            return DynoResult.FromAffected(0);

        return await RetryPolicy.ExecuteAsync(async () =>
        {
            // Infer columns from first row's properties
            var props = rowList[0].GetType().GetProperties();
            var columns = props.Select(p => p.Name).ToList();

            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);

            var totalAffected = 0;

            // Batch in groups of 500 rows to avoid SQL parameter limits
            foreach (var batch in rowList.Chunk(500))
            {
                var paramDict = new Dictionary<string, object?>();
                var sql = BuildBulkInsertSql(tableName, columns, batch, props, paramDict);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandType = CommandType.Text;

                foreach (var kv in paramDict)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = kv.Key;
                    p.Value = kv.Value ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }

                totalAffected += await cmd.ExecuteNonQueryAsync(ct);
            }

            return DynoResult.FromAffected(totalAffected);
        }, _options.Retry, ct);
    }

    // ── Pagination ────────────────────────────────────────────────────

    public async Task<PagedDynoResult> QueryPagedAsync(
        string query, int pageNumber, int pageSize,
        object? parameters = null, CancellationToken ct = default)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;

        return await RetryPolicy.ExecuteAsync(async () =>
        {
            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);

            // Total count — wrap original query in COUNT(*)
            var countSql = $"SELECT COUNT(*) FROM ({query}) AS __dyno_count__";
            await using var countCmd = BuildCommand(conn, null, countSql, CommandType.Text, parameters);
            var countResult = await countCmd.ExecuteScalarAsync(ct);
            var totalCount = Convert.ToInt32(countResult);

            // Paged data — append OFFSET/FETCH to original query
            var offset = (pageNumber - 1) * pageSize;
            var pagedSql = $"{query} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

            await using var dataCmd = BuildCommand(conn, null, pagedSql, CommandType.Text, parameters);
            await using var reader = await dataCmd.ExecuteReaderAsync(ct);
            var rows = await DynoReader.ReadAllAsync(reader, ct);

            return new PagedDynoResult
            {
                Data = DynoResult.FromRows(rows),
                TotalCount = totalCount,
                CurrentPage = pageNumber,
                PageSize = pageSize
            };
        }, _options.Retry, ct);
    }

    // ── Multi Result Set ──────────────────────────────────────────────

    public async Task<IReadOnlyList<DynoResult>> QueryMultipleAsync(
        string query, object? parameters = null,
        CommandType commandType = CommandType.Text, CancellationToken ct = default)
    {
        return await RetryPolicy.ExecuteAsync(async () =>
        {
            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = BuildCommand(conn, null, query, commandType, parameters);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var allSets = await DynoReader.ReadMultipleAsync(reader, ct);
            var results = new List<DynoResult>(allSets.Count);
            foreach (var rows in allSets)
                results.Add(DynoResult.FromRows(rows));
            return results;
        }, _options.Retry, ct);
    }

    // ── Transactions ──────────────────────────────────────────────────

    public async Task<IDynoTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        var conn = _factory.CreateConnection();
        await conn.OpenAsync(ct);
        var tx = await conn.BeginTransactionAsync(isolationLevel, ct);
        return new DynoTransaction(conn, tx, _options);
    }

    // ── Internals ─────────────────────────────────────────────────────

    private async Task<DynoResult> ExecuteReaderAsync(
        string query, object? parameters, CommandType commandType, CancellationToken ct)
    {
        await using var conn = _factory.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = BuildCommand(conn, null, query, commandType, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = await DynoReader.ReadAllAsync(reader, ct);
        return DynoResult.FromRows(rows);
    }

    private async Task<DynoResult> ExecuteNonQueryAsync(
        string query, object? parameters, CommandType commandType, CancellationToken ct)
    {
        await using var conn = _factory.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = BuildCommand(conn, null, query, commandType, parameters);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return DynoResult.FromAffected(affected);
    }

    internal static DbCommand BuildCommand(
        DbConnection conn,
        DbTransaction? tx,
        string query,
        CommandType commandType,
        object? parameters)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = query;
        cmd.CommandType = commandType;
        DynoReader.BindParameters(cmd, parameters);
        return cmd;
    }

    private static string BuildBulkInsertSql(
        string tableName,
        List<string> columns,
        object[] batch,
        System.Reflection.PropertyInfo[] props,
        Dictionary<string, object?> paramDict)
    {
        var sql = new StringBuilder();

        sql.Append($"INSERT INTO {tableName} (");
        sql.Append(string.Join(", ", columns));
        sql.Append(") VALUES ");

        var rowSql = new List<string>();

        for (var i = 0; i < batch.Length; i++)
        {
            var colParams = new List<string>();
            foreach (var prop in props)
            {
                var paramName = $"@{prop.Name}_{i}";
                colParams.Add(paramName);
                paramDict[paramName] = prop.GetValue(batch[i]) ?? DBNull.Value;
            }
            rowSql.Add($"({string.Join(", ", colParams)})");
        }

        sql.Append(string.Join(", ", rowSql));
        return sql.ToString();
    }
}