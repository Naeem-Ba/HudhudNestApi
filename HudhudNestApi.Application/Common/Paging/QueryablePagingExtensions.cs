namespace HudhudNestApi.Application.Common.Paging;

/// <summary>
/// One place for the page-to-offset arithmetic. <c>(page - 1) * pageSize</c> in
/// <see cref="int"/> overflows for a large <c>page</c> (e.g. 2147483647) and
/// PostgreSQL then rejects the negative OFFSET with a 500 (launch audit A2-005).
/// </summary>
public static class QueryablePagingExtensions
{
    /// <summary>Offset for a 1-based page, computed in 64 bits and capped at <see cref="int.MaxValue"/>.</summary>
    public static int PageOffset(int page, int pageSize)
    {
        var offset = ((long)Math.Max(page, 1) - 1) * Math.Max(pageSize, 0);
        return offset > int.MaxValue ? int.MaxValue : (int)offset;
    }

    /// <summary>Skips the rows before the given 1-based page without overflowing.</summary>
    public static IQueryable<T> SkipPage<T>(this IQueryable<T> query, int page, int pageSize)
        => query.Skip(PageOffset(page, pageSize));

    /// <summary>In-memory counterpart: an overflowed negative offset would silently return page 1.</summary>
    public static IEnumerable<T> SkipPage<T>(this IEnumerable<T> source, int page, int pageSize)
        => source.Skip(PageOffset(page, pageSize));
}
