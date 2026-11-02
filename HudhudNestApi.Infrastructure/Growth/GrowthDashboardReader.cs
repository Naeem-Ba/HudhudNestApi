using HudhudNestApi.Application.Growth;
using HudhudNestApi.Domain.Growth.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HudhudNestApi.Infrastructure.Growth;

/// <summary>
/// Growth engine P10. A handful of grouped queries over <c>GrowthEvents</c> (indexed by name and time, and by
/// user and time) and <c>UserAccounts</c>, for an admin-only page and a window of at most 186 days. Returns
/// counts only: no names, e-mails or ids.
/// </summary>
public sealed class GrowthDashboardReader : IGrowthDashboardReader
{
    internal const int MaxCampaignRows = 50;
    internal static readonly int[] RetentionDays = [1, 7, 30];

    private static readonly string[] ActionNames =
    [
        GrowthEventNames.ListingPublished, GrowthEventNames.SearchRequestCreated, GrowthEventNames.SavedSearchCreated,
        GrowthEventNames.FavoriteAdded, GrowthEventNames.ContactStarted,
    ];

    private readonly AppDbContext _db;

    public GrowthDashboardReader(AppDbContext db) => _db = db;

    public async Task<GrowthOverviewDto> GetOverviewAsync(DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct)
    {
        var events = _db.Set<GrowthEvent>().AsNoTracking();

        var accountsPerDay = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.CreatedAt >= fromUtc && u.CreatedAt < toUtc)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var eventsPerDay = await events
            .Where(e => e.OccurredAt >= fromUtc && e.OccurredAt < toUtc)
            .GroupBy(e => new { e.Name, Day = e.OccurredAt.Date })
            .Select(g => new { g.Key.Name, g.Key.Day, Count = g.Count() })
            .ToListAsync(ct);

        var activeUsers = await events
            .Where(e => e.Name == GrowthEventNames.UserActive && e.OccurredAt >= fromUtc && e.OccurredAt < toUtc && e.UserId != null)
            .Select(e => e.UserId).Distinct().CountAsync(ct);

        var activitySince = await events
            .Where(e => e.Name == GrowthEventNames.UserActive)
            .MinAsync(e => (DateTime?)e.OccurredAt, ct);

        // Sign-up cohort of the period with what each account did in its first 30 days and its last active day.
        var cohort = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.CreatedAt >= fromUtc && u.CreatedAt < toUtc)
            .Select(u => new
            {
                u.CreatedAt,
                Activated = events.Any(e => e.UserId == u.Id && ActionNames.Contains(e.Name)
                    && e.OccurredAt >= u.CreatedAt && e.OccurredAt < u.CreatedAt.AddDays(30)),
                Published = events.Any(e => e.UserId == u.Id && e.Name == GrowthEventNames.ListingPublished
                    && e.OccurredAt >= u.CreatedAt && e.OccurredAt < u.CreatedAt.AddDays(30)),
                Contacted = events.Any(e => e.UserId == u.Id && e.Name == GrowthEventNames.ContactStarted
                    && e.OccurredAt >= u.CreatedAt && e.OccurredAt < u.CreatedAt.AddDays(30)),
                LastActive = events.Where(e => e.UserId == u.Id && e.Name == GrowthEventNames.UserActive)
                    .Max(e => (DateTime?)e.OccurredAt),
            })
            .ToListAsync(ct);

        var registrations = await events
            .Where(e => e.Name == GrowthEventNames.UserRegistered && e.OccurredAt >= fromUtc && e.OccurredAt < toUtc && e.UserId != null)
            .Select(e => new
            {
                e.Source,
                e.Medium,
                e.Campaign,
                Activated = events.Any(x => x.UserId == e.UserId && ActionNames.Contains(x.Name)
                    && x.OccurredAt >= e.OccurredAt && x.OccurredAt < e.OccurredAt.AddDays(30)),
                Published = events.Any(x => x.UserId == e.UserId && x.Name == GrowthEventNames.ListingPublished
                    && x.OccurredAt >= e.OccurredAt && x.OccurredAt < e.OccurredAt.AddDays(30)),
                Contacted = events.Any(x => x.UserId == e.UserId && x.Name == GrowthEventNames.ContactStarted
                    && x.OccurredAt >= e.OccurredAt && x.OccurredAt < e.OccurredAt.AddDays(30)),
            })
            .ToListAsync(ct);

        int Count(string name) => eventsPerDay.Where(e => e.Name == name).Sum(e => e.Count);
        int On(string name, DateTime day) => eventsPerDay.Where(e => e.Name == name && e.Day == day).Sum(e => e.Count);

        var days = new List<GrowthDayDto>();
        for (var day = fromUtc.Date; day < toUtc; day = day.AddDays(1))
        {
            days.Add(new GrowthDayDto(
                DateOnly.FromDateTime(day),
                accountsPerDay.Where(a => a.Day == day).Sum(a => a.Count),
                On(GrowthEventNames.ListingPublished, day),
                On(GrowthEventNames.SearchRequestCreated, day),
                On(GrowthEventNames.SavedSearchCreated, day),
                On(GrowthEventNames.FavoriteAdded, day),
                On(GrowthEventNames.ContactStarted, day),
                On(GrowthEventNames.UserActive, day)));
        }

        var campaigns = registrations
            .Where(r => r.Source != null || r.Medium != null || r.Campaign != null)
            .GroupBy(r => (r.Source, r.Medium, r.Campaign))
            .Select(g => new GrowthCampaignDto(g.Key.Source, g.Key.Medium, g.Key.Campaign, g.Count(),
                g.Count(r => r.Activated), g.Count(r => r.Published), g.Count(r => r.Contacted)))
            .OrderByDescending(c => c.Registered).ThenBy(c => c.Source).ThenBy(c => c.Campaign)
            .Take(MaxCampaignRows)
            .ToList();

        // Only accounts created after activity tracking started can be measured; each N needs accounts N days old.
        var measurable = activitySince is null ? [] : cohort.Where(c => c.CreatedAt >= activitySince.Value.Date).ToList();
        var retention = RetentionDays.Select(n =>
        {
            var old = measurable.Where(c => c.CreatedAt.Date.AddDays(n) <= nowUtc.Date).ToList();
            return new RetentionDto(n, old.Count, old.Count(c => c.LastActive.HasValue && c.LastActive.Value.Date >= c.CreatedAt.Date.AddDays(n)));
        }).ToList();

        return new GrowthOverviewDto(
            fromUtc, toUtc,
            new GrowthTotalsDto(
                accountsPerDay.Sum(a => a.Count),
                Count(GrowthEventNames.ListingPublished),
                Count(GrowthEventNames.SearchRequestCreated),
                Count(GrowthEventNames.SavedSearchCreated),
                Count(GrowthEventNames.FavoriteAdded),
                Count(GrowthEventNames.ContactStarted),
                Count(GrowthEventNames.ListingProposed),
                Count(GrowthEventNames.ReferralQualified),
                activeUsers),
            new GrowthFunnelDto(cohort.Count, cohort.Count(c => c.Activated), cohort.Count(c => c.Published), cohort.Count(c => c.Contacted)),
            days,
            campaigns,
            retention,
            activitySince,
            registrations.Count(r => r.Source == null && r.Medium == null && r.Campaign == null));
    }
}
