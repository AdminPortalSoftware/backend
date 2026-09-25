namespace Platform.Application.Pagination;

/// <summary>Offset pagination request (admin grids). Values are clamped to safe bounds.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = 25)
{
    public const int MaxPageSize = 200;

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
}

/// <summary>
/// Keyset (cursor) pagination for feeds and mobile clients: stable under concurrent inserts
/// and O(1) regardless of depth. The cursor is opaque to clients.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
