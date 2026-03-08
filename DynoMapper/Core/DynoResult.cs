using System.Dynamic;

namespace DynoMapper.Core;

/// <summary>
/// The universal result type returned by every DynoMapper query.
///
/// Access patterns:
///   .List              → IReadOnlyList of dynamic rows
///   .Single            → first row as dynamic (null if empty)
///   .Scalar&lt;T&gt;()       → typed scalar value
///   .AffectedRows      → INSERT/UPDATE/DELETE count
///   .Pick(...)         → keep only named columns
///   .Compute(...)      → add derived/calculated fields
///   .Where(...)        → filter rows by field value
///   .Select(...)       → project one column to flat list
///   .OrderBy(...)      → sort rows by field at runtime
///   .GroupBy(...)      → group rows by field at runtime
///   .Columns           → all column names from first row
///   .ToDictionaryList()→ Dictionary&lt;string,object?&gt; list
/// </summary>
public sealed class DynoResult
{
    private readonly List<ExpandoObject> _rows;

    internal DynoResult(List<ExpandoObject> rows) => _rows = rows;

    // ── Factories ─────────────────────────────────────────────────────
    internal static DynoResult FromRows(List<ExpandoObject> rows) => new(rows);
    internal static DynoResult FromScalar(object? value) => new([]) { ScalarValue = value };
    internal static DynoResult FromAffected(int count) => new([]) { AffectedRows = count };

    // ── Core access ───────────────────────────────────────────────────

    /// <summary>All rows as a list of dynamic objects.</summary>
    // ✅ FIXED: Cast required — List<ExpandoObject> cannot implicitly become IReadOnlyList<dynamic>
    public IReadOnlyList<dynamic> List => _rows.Cast<dynamic>().ToList();

    /// <summary>First row as dynamic. Null if no rows returned.</summary>
    public dynamic? Single => _rows.FirstOrDefault();

    /// <summary>Raw scalar value from ExecuteScalar.</summary>
    public object? ScalarValue { get; private init; }

    /// <summary>
    /// Typed scalar value. Use after QueryScalarAsync.
    /// e.g. result.Scalar&lt;int&gt;() for COUNT(*).
    /// </summary>
    public T? Scalar<T>()
    {
        if (ScalarValue is null || ScalarValue is DBNull) return default;
        return (T)Convert.ChangeType(ScalarValue, typeof(T));
    }

    /// <summary>Rows affected by INSERT / UPDATE / DELETE.</summary>
    public int AffectedRows { get; private init; }

    /// <summary>True if any rows were returned.</summary>
    public bool HasRows => _rows.Count > 0;

    /// <summary>Number of rows returned.</summary>
    public int RowCount => _rows.Count;

    /// <summary>Access a row by zero-based index.</summary>
    public dynamic this[int index] => _rows[index];

    // ── Runtime shaping ───────────────────────────────────────────────

    /// <summary>
    /// Keep only specified columns — all others are dropped.
    /// Great for returning slim API responses without creating a DTO.
    /// e.g. result.Pick("Id", "Name", "Email")
    /// </summary>
    public DynoResult Pick(params string[] fields)
    {
        var filtered = new List<ExpandoObject>(_rows.Count);

        foreach (var row in _rows)
        {
            var src = (IDictionary<string, object?>)row;
            var newRow = (IDictionary<string, object?>)new ExpandoObject();
            foreach (var f in fields)
                if (src.TryGetValue(f, out var val))
                    newRow[f] = val;
            filtered.Add((ExpandoObject)newRow);
        }

        return new DynoResult(filtered);
    }

    /// <summary>
    /// Add or override a computed/derived field on every row at runtime.
    /// No model class needed.
    /// e.g. result.Compute("FullName", row => $"{row.FirstName} {row.LastName}")
    ///       .Compute("Age", row => DateTime.Today.Year - ((DateTime)row.DOB).Year)
    /// </summary>
    public DynoResult Compute(string fieldName, Func<dynamic, object?> compute)
    {
        foreach (var row in _rows)
            ((IDictionary<string, object?>)row)[fieldName] = compute(row);
        return this;
    }

    /// <summary>
    /// Filter rows where a named field equals a value.
    /// e.g. result.Where("Status", "Active")
    /// </summary>
    public IReadOnlyList<dynamic> Where(string field, object? value)
    {
        var list = new List<dynamic>(_rows.Count);

        foreach (var row in _rows)
        {
            var d = (IDictionary<string, object?>)row;
            if (d.TryGetValue(field, out var val) && Equals(val, value))
                list.Add(row);
        }

        return list;
    }

    /// <summary>
    /// Project a single column to a flat list.
    /// e.g. result.Select("UserId") → [1, 2, 3, 4]
    /// </summary>
    public IReadOnlyList<object?> Select(string field)
    {
        var list = new List<object?>(_rows.Count);

        foreach (var row in _rows)
        {
            var d = (IDictionary<string, object?>)row;
            list.Add(d.TryGetValue(field, out var val) ? val : null);
        }

        return list;
    }

    /// <summary>
    /// Sort rows by a field name at runtime.
    /// e.g. result.OrderBy("CreatedAt", descending: true)
    /// </summary>
    public DynoResult OrderBy(string field, bool descending = false)
    {
        var list = new List<ExpandoObject>(_rows);

        list.Sort((a, b) =>
        {
            var valA = GetField(a, field) as IComparable;
            var valB = GetField(b, field) as IComparable;

            if (valA is null && valB is null) return 0;
            if (valA is null) return descending ? 1 : -1;
            if (valB is null) return descending ? -1 : 1;

            return descending ? valB.CompareTo(valA) : valA.CompareTo(valB);
        });

        return new DynoResult(list);
    }

    /// <summary>
    /// Group rows by a field value at runtime.
    /// Returns a dictionary where each key is a distinct field value
    /// and each value is a DynoResult containing the matching rows.
    ///
    /// e.g. var grouped = result.GroupBy("DepartmentId");
    ///      foreach (var (deptId, rows) in grouped)
    ///          Console.WriteLine($"Dept {deptId}: {rows.RowCount} employees");
    /// </summary>
    public IReadOnlyDictionary<object, DynoResult> GroupBy(string field)
    {
        var dict = new Dictionary<object, DynoResult>();

        // ✅ FIXED: ?? "(null)" ensures key is always non-null — no nullable warning
        foreach (var group in _rows.GroupBy(row => GetField(row, field) ?? "(null)"))
        {
            dict[group.Key] = new DynoResult(new List<ExpandoObject>(group));
        }

        return dict;
    }

    // ── Introspection ─────────────────────────────────────────────────

    /// <summary>All column names from the first row. Useful for debugging.</summary>
    public IReadOnlyList<string> Columns
        => _rows.Count > 0
            ? ((IDictionary<string, object?>)_rows[0]).Keys.ToList()
            : Array.Empty<string>();

    /// <summary>
    /// Convert result to a list of plain dictionaries.
    /// Use when you need string-keyed access instead of dynamic.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> ToDictionaryList()
    {
        var list = new List<Dictionary<string, object?>>(_rows.Count);

        foreach (var row in _rows)
            list.Add(new Dictionary<string, object?>((IDictionary<string, object?>)row));

        return list;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static object? GetField(ExpandoObject row, string field)
    {
        var d = (IDictionary<string, object?>)row;
        return d.TryGetValue(field, out var val) ? val : null;
    }
}