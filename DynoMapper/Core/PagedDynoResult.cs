namespace DynoMapper.Core;

/// <summary>
/// Returned by QueryPagedAsync — wraps a DynoResult page
/// with total count and pagination metadata.
///
/// <code>
/// var page = await _sql.QueryPagedAsync(
///     "SELECT * FROM Users ORDER BY Id",
///     pageNumber: 1, pageSize: 20);
///
/// // page.Data.List       → current page rows
/// // page.TotalCount      → total matching rows
/// // page.TotalPages      → how many pages exist
/// // page.CurrentPage     → current page number (1-based)
/// // page.HasNextPage     → bool
/// // page.HasPreviousPage → bool
/// </code>
/// </summary>
public sealed class PagedDynoResult
{
    /// <summary>The rows for the current page.</summary>
    public DynoResult Data { get; init; } = default!;

    /// <summary>Total number of rows across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Current page number (1-based).</summary>
    public int CurrentPage { get; init; }

    /// <summary>Number of rows per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>True if there is a page after this one.</summary>
    public bool HasNextPage => CurrentPage < TotalPages;

    /// <summary>True if there is a page before this one.</summary>
    public bool HasPreviousPage => CurrentPage > 1;

    /// <summary>First row number on this page (for display: "Showing 21–40").</summary>
    public int FirstRow => (CurrentPage - 1) * PageSize + 1;

    /// <summary>Last row number on this page.</summary>
    public int LastRow => Math.Min(CurrentPage * PageSize, TotalCount);
}
