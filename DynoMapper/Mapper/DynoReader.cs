using System.Data.Common;
using System.Dynamic;

namespace DynoMapper.Mapper;

/// <summary>
/// Internal engine that converts any DbDataReader into a list of ExpandoObjects.
/// This is the heart of DynoMapper — it runs at runtime so you never need model classes.
/// </summary>
internal static class DynoReader
{
    /// <summary>
    /// Reads all rows from a DbDataReader into a list of dynamic ExpandoObjects.
    /// Each column in the result set becomes a property on the dynamic object.
    /// Column names are preserved exactly as returned by the database.
    /// </summary>
    internal static async Task<List<ExpandoObject>> ReadAllAsync(
        DbDataReader reader,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ExpandoObject>();

        // Capture schema once — not per row
        var schema = BuildSchema(reader);

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = (IDictionary<string, object?>)new ExpandoObject();

            foreach (var (name, ordinal) in schema)
            {
                row[name] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            }

            result.Add((ExpandoObject)row);
        }

        return result;
    }

    /// <summary>
    /// Reads multiple result sets from a single query.
    /// Returns a list of DynoResult — one per result set.
    /// Use with stored procedures or multi-SELECT queries.
    /// </summary>
    internal static async Task<List<List<ExpandoObject>>> ReadMultipleAsync(
        DbDataReader reader,
        CancellationToken cancellationToken = default)
    {
        var allResultSets = new List<List<ExpandoObject>>();

        do
        {
            var schema = BuildSchema(reader);
            var rows = new List<ExpandoObject>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var row = (IDictionary<string, object?>)new ExpandoObject();
                foreach (var (name, ordinal) in schema)
                {
                    row[name] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
                }
                rows.Add((ExpandoObject)row);
            }

            allResultSets.Add(rows);

        } while (await reader.NextResultAsync(cancellationToken));

        return allResultSets;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Captures column name → ordinal mapping once per query.
    /// Avoids repeated GetName() calls per row for performance.
    /// </summary>
    private static List<(string Name, int Ordinal)> BuildSchema(DbDataReader reader)
    {
        var schema = new List<(string, int)>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
            schema.Add((reader.GetName(i), i));
        return schema;
    }

    /// <summary>
    /// Adds SQL parameters from an anonymous object to any DbCommand.
    /// e.g. new { UserId = 1, Status = "Active" } → @UserId, @Status
    /// </summary>
    internal static void BindParameters(DbCommand command, object? parameters)
    {
        if (parameters is null) return;

        foreach (var prop in parameters.GetType().GetProperties())
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@{prop.Name}";
            param.Value = prop.GetValue(parameters) ?? DBNull.Value;
            command.Parameters.Add(param);
        }
    }
}
