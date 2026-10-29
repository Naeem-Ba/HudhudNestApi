using Microsoft.EntityFrameworkCore;
using Npgsql;
using HudhudNestApi.Application.Listings.Views;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Listings;

public sealed class ListingViewRepository : IListingViewRepository
{
    private readonly AppDbContext _db;

    public ListingViewRepository(AppDbContext db) => _db = db;

    public async Task<ListingViewTarget?> GetTargetAsync(ListingKind kind, Guid listingId, CancellationToken ct = default)
    {
        if (kind == ListingKind.Property)
        {
            var now = DateTime.UtcNow;
            return await _db.Properties.AsNoTracking()
                .Where(p => p.Id == listingId)
                .Select(p => new ListingViewTarget(
                    p.OwnerId, p.AgencyId, p.Title,
                    // The same rule the detail page uses (Property.PubliclyViewable), evaluated in SQL.
                    p.IsPublished &&
                    (((p.Status == Domain.Enums.PropertyStatus.Available || p.Status == Domain.Enums.PropertyStatus.Reserved) &&
                      (p.ExpiresAt == null || p.ExpiresAt > now)) ||
                     p.Status == Domain.Enums.PropertyStatus.Sold ||
                     p.Status == Domain.Enums.PropertyStatus.Rented)))
                .FirstOrDefaultAsync(ct);
        }

        return await _db.ShortStayListings.AsNoTracking()
            .Where(l => l.Id == listingId)
            .Select(l => new ListingViewTarget(l.OwnerId, null, l.Title, l.IsPublished))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GetCountsAsync(
        ListingKind kind, IReadOnlyCollection<Guid> listingIds, CancellationToken ct = default)
    {
        if (listingIds.Count == 0)
            return new Dictionary<Guid, long>();

        var ids = listingIds.Distinct().ToList();
        return await _db.ListingViewCounters.AsNoTracking()
            .Where(c => c.Kind == kind && ids.Contains(c.ListingId))
            .ToDictionaryAsync(c => c.ListingId, c => c.ViewCount, ct);
    }

    public Task<Guid?> GetUserAgencyIdAsync(Guid userId, CancellationToken ct = default)
        => _db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AgencyId)
            .FirstOrDefaultAsync(ct);

    public async Task<(bool Counted, long TotalViews)> TryRecordAsync(
        ListingKind kind, Guid listingId, string viewerKeyHash, long bucket, DateTime nowUtc, CancellationToken ct = default)
    {
        // One statement, so the ledger insert and the counter increment commit together or not at
        // all, and the increment only happens when the insert really inserted (no conflict on the
        // dedupe key). The counter row is created on the first view and incremented atomically after.
        const string sql = """
            WITH ins AS (
                INSERT INTO "ListingViewEvents" ("Id", "Kind", "ListingId", "ViewerKeyHash", "Bucket", "ViewedAt")
                VALUES (@id, @kind, @listing, @hash, @bucket, @now)
                ON CONFLICT ("Kind", "ListingId", "ViewerKeyHash", "Bucket") DO NOTHING
                RETURNING 1)
            INSERT INTO "ListingViewCounters" ("Kind", "ListingId", "ViewCount", "UpdatedAt")
            SELECT @kind, @listing, 1, @now WHERE EXISTS (SELECT 1 FROM ins)
            ON CONFLICT ("Kind", "ListingId")
            DO UPDATE SET "ViewCount" = "ListingViewCounters"."ViewCount" + 1, "UpdatedAt" = @now
            RETURNING "ViewCount" AS "Value"
            """;

        var updated = await _db.Database.SqlQueryRaw<long>(sql,
                new NpgsqlParameter("id", Guid.NewGuid()),
                new NpgsqlParameter("kind", kind.ToString()),
                new NpgsqlParameter("listing", listingId),
                new NpgsqlParameter("hash", viewerKeyHash),
                new NpgsqlParameter("bucket", bucket),
                new NpgsqlParameter("now", DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified)))
            .ToListAsync(ct);

        if (updated.Count > 0)
            return (true, updated[0]);

        var counts = await GetCountsAsync(kind, new[] { listingId }, ct);
        return (false, counts.TryGetValue(listingId, out var total) ? total : 0);
    }

    public async Task<IReadOnlyList<ListingViewStatsRow>> GetOwnerStatsAsync(Guid ownerId, DateTime nowUtc, CancellationToken ct = default)
    {
        var properties = await _db.Properties.AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .Select(p => new ListingBase(ListingKind.Property, p.Id, p.Title))
            .ToListAsync(ct);
        var stays = await _db.ShortStayListings.AsNoTracking()
            .Where(l => l.OwnerId == ownerId)
            .Select(l => new ListingBase(ListingKind.ShortStay, l.Id, l.Title))
            .ToListAsync(ct);

        return await WithWindowsAsync(properties.Concat(stays).ToList(), nowUtc, ct);
    }

    public async Task<IReadOnlyList<ListingViewStatsRow>> GetAgencyStatsAsync(Guid agencyId, DateTime nowUtc, CancellationToken ct = default)
    {
        var properties = await _db.Properties.AsNoTracking()
            .Where(p => p.AgencyId == agencyId)
            .Select(p => new ListingBase(ListingKind.Property, p.Id, p.Title))
            .ToListAsync(ct);

        return await WithWindowsAsync(properties, nowUtc, ct);
    }

    public async Task<IReadOnlyDictionary<DateOnly, int>> GetDailyViewsAsync(
        ListingKind kind, Guid listingId, DateTime fromUtc, CancellationToken ct = default)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Unspecified);
        var rows = await _db.Set<ListingViewEvent>().AsNoTracking()
            .Where(e => e.Kind == kind && e.ListingId == listingId && e.ViewedAt >= from)
            .GroupBy(e => e.ViewedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => r.Count);
    }

    public Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        var cutoff = DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Unspecified);
        return _db.Database.ExecuteSqlAsync($"""
            DELETE FROM "ListingViewEvents"
            WHERE "Id" IN (SELECT "Id" FROM "ListingViewEvents" WHERE "ViewedAt" < {cutoff} LIMIT {batchSize})
            """, ct);
    }

    private async Task<IReadOnlyList<ListingViewStatsRow>> WithWindowsAsync(
        List<ListingBase> listings, DateTime nowUtc, CancellationToken ct)
    {
        if (listings.Count == 0)
            return Array.Empty<ListingViewStatsRow>();

        var ids = listings.Select(l => l.Id).ToList();
        var since30 = DateTime.SpecifyKind(nowUtc.AddDays(-30), DateTimeKind.Unspecified);
        var since7 = DateTime.SpecifyKind(nowUtc.AddDays(-7), DateTimeKind.Unspecified);

        var windows = await _db.Set<ListingViewEvent>().AsNoTracking()
            .Where(e => ids.Contains(e.ListingId) && e.ViewedAt >= since30)
            .GroupBy(e => new { e.Kind, e.ListingId })
            .Select(g => new
            {
                g.Key.Kind,
                g.Key.ListingId,
                Views30 = g.Count(),
                Views7 = g.Count(e => e.ViewedAt >= since7),
                Unique30 = g.Select(e => e.ViewerKeyHash).Distinct().Count()
            })
            .ToListAsync(ct);

        var totals = await _db.ListingViewCounters.AsNoTracking()
            .Where(c => ids.Contains(c.ListingId))
            .ToDictionaryAsync(c => (c.Kind, c.ListingId), c => c.ViewCount, ct);

        var byKey = windows.ToDictionary(w => (w.Kind, w.ListingId));
        return listings
            .Select(l =>
            {
                var total = totals.TryGetValue((l.Kind, l.Id), out var t) ? t : 0;
                return byKey.TryGetValue((l.Kind, l.Id), out var w)
                    ? new ListingViewStatsRow(l.Kind, l.Id, l.Title, total, w.Views7, w.Views30, w.Unique30)
                    : new ListingViewStatsRow(l.Kind, l.Id, l.Title, total, 0, 0, 0);
            })
            .ToList();
    }

    private sealed record ListingBase(ListingKind Kind, Guid Id, string Title);
}
