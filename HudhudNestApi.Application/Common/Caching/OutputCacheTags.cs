namespace HudhudNestApi.Application.Common.Caching;

/// <summary>
/// Output-cache tag names shared between where the HTTP cache is populated (the Web layer's
/// OutputCacheRegistration, which tags the public property list/detail responses) and where it
/// must be evicted (AppDbContext.SaveChangesAsync, in Infrastructure, which sees every actual
/// write). A single source of truth here means the tag name used to populate the cache and the
/// one used to evict it can never silently drift apart.
/// </summary>
public static class OutputCacheTags
{
    public const string Properties = "properties";
}
