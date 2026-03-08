using DynoMapper.Core;
using DynoMapper.Mapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DynoMapper.Extensions;

/// <summary>
/// EF Core extension — run raw SQL through your existing DbContext
/// and get back a fully dynamic DynoResult. No model class needed.
///
/// Usage:
/// <code>
/// var result = await _context.DynoQueryAsync(
///     "SELECT Id, Name FROM Users WHERE IsActive = @IsActive",
///     new { IsActive = true });
///
/// foreach (var row in result.List)
///     Console.WriteLine(row.Name);
/// </code>
/// </summary>
public static class EFCoreDynoExtensions
{
    /// <summary>
    /// Execute raw SQL on any DbContext and return a dynamic DynoResult.
    /// Parameters are passed as anonymous object: new { Id = 1, Status = "Active" }
    /// </summary>
    public static async Task<DynoResult> DynoQueryAsync(
        this DbContext context,
        string sql,
        object? parameters = null,
        CancellationToken ct = default)
    {
        var conn = context.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;

        if (!wasOpen)
            await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = System.Data.CommandType.Text;

            // Enlist in EF Core's current transaction if one exists
            var efTx = context.Database.CurrentTransaction?.GetDbTransaction();
            if (efTx is not null)
                cmd.Transaction = efTx;

            DynoReader.BindParameters(cmd, parameters);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = await DynoReader.ReadAllAsync(reader, ct);
            return DynoResult.FromRows(rows);
        }
        finally
        {
            if (!wasOpen)
                await conn.CloseAsync();
        }
    }

    /// <summary>
    /// Execute a raw stored procedure on any DbContext and return a dynamic DynoResult.
    /// </summary>
    public static async Task<DynoResult> DynoQuerySpAsync(
        this DbContext context,
        string procedureName,
        object? parameters = null,
        CancellationToken ct = default)
    {
        var conn = context.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;

        if (!wasOpen)
            await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = procedureName;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            var efTx = context.Database.CurrentTransaction?.GetDbTransaction();
            if (efTx is not null)
                cmd.Transaction = efTx;

            DynoReader.BindParameters(cmd, parameters);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = await DynoReader.ReadAllAsync(reader, ct);
            return DynoResult.FromRows(rows);
        }
        finally
        {
            if (!wasOpen)
                await conn.CloseAsync();
        }
    }

    /// <summary>
    /// Execute raw SQL for INSERT/UPDATE/DELETE via EF Core's connection.
    /// Returns DynoResult.AffectedRows.
    /// </summary>
    public static async Task<DynoResult> DynoExecuteAsync(
        this DbContext context,
        string sql,
        object? parameters = null,
        CancellationToken ct = default)
    {
        var conn = context.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;

        if (!wasOpen)
            await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var efTx = context.Database.CurrentTransaction?.GetDbTransaction();
            if (efTx is not null)
                cmd.Transaction = efTx;

            DynoReader.BindParameters(cmd, parameters);

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            return DynoResult.FromAffected(affected);
        }
        finally
        {
            if (!wasOpen)
                await conn.CloseAsync();
        }
    }
}
