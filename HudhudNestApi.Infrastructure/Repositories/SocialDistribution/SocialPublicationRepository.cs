using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories.SocialDistribution;

public sealed class SocialPublicationRepository : ISocialPublicationRepository
{
    private readonly AppDbContext _db;

    public SocialPublicationRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SocialPublication publication, CancellationToken ct = default) =>
        await _db.SocialPublications.AddAsync(publication, ct);

    public Task<SocialPublication?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SocialPublications.Include(p => p.Content).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<PagedResult<SocialPublication>> GetPagedAsync(SocialPublicationFilterDto filter, CancellationToken ct = default)
    {
        var query = _db.SocialPublications.AsNoTracking().Include(p => p.Content).AsQueryable();

        if (filter.PropertyId is not null)
            query = query.Where(p => p.PropertyId == filter.PropertyId);

        if (filter.SocialAccountId is not null)
            query = query.Where(p => p.SocialAccountId == filter.SocialAccountId);

        if (filter.Status is not null)
            query = query.Where(p => p.Status == filter.Status);

        if (filter.Platform is not null)
            query = query.Where(p => p.Content != null && p.Content.Platform == filter.Platform);

        if (filter.FromDate is not null)
            query = query.Where(p => p.CreatedAt >= filter.FromDate);

        if (filter.ToDate is not null)
            query = query.Where(p => p.CreatedAt <= filter.ToDate);

        if (filter.DistributionRuleId is not null)
            query = query.Where(p => p.DistributionRuleId == filter.DistributionRuleId);

        if (filter.GovernorateId is not null)
        {
            var accountIdsInGovernorate = _db.SocialAccounts
                .Where(a => a.GovernorateId == filter.GovernorateId)
                .Select(a => a.Id);
            query = query.Where(p => accountIdsInGovernorate.Contains(p.SocialAccountId));
        }

        var totalCount = await query.CountAsync(ct);

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<SocialPublication>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<IReadOnlyList<SocialPublication>> GetDueToPublishAsync(DateTime utcNow, int take, CancellationToken ct = default) =>
        await _db.SocialPublications
            .Include(p => p.Content)
            .Where(p =>
                p.Status == SocialPublicationStatus.Queued &&
                (p.ScheduledAt == null || p.ScheduledAt <= utcNow))
            .OrderBy(p => p.ScheduledAt ?? p.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SocialPublication>> GetDueForRetryAsync(DateTime utcNow, int take, CancellationToken ct = default) =>
        await _db.SocialPublications
            .Include(p => p.Content)
            .Where(p =>
                p.Status == SocialPublicationStatus.Retrying &&
                (p.NextRetryAt == null || p.NextRetryAt <= utcNow))
            .OrderBy(p => p.NextRetryAt ?? p.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SocialPublication>> GetPublishingWithExpiredLeaseAsync(DateTime utcNow, int take, CancellationToken ct = default) =>
        await _db.SocialPublications
            .Include(p => p.Content)
            .Where(p =>
                p.Status == SocialPublicationStatus.Publishing &&
                p.LeaseUntil != null &&
                p.LeaseUntil <= utcNow)
            .OrderBy(p => p.LeaseUntil)
            .Take(take)
            .ToListAsync(ct);

    public Task<bool> ExistsActiveForPropertyAndAccountAsync(Guid propertyId, Guid socialAccountId, CancellationToken ct = default) =>
        _db.SocialPublications.AnyAsync(p =>
            p.PropertyId == propertyId &&
            p.SocialAccountId == socialAccountId &&
            p.DistributionRuleId != null &&
            p.Status != SocialPublicationStatus.Cancelled,
            ct);

    public async Task<IReadOnlyList<SocialPublication>> GetActiveForPropertyAsync(Guid propertyId, CancellationToken ct = default) =>
        await _db.SocialPublications
            .Include(p => p.Content)
            .Where(p => p.PropertyId == propertyId && p.Status == SocialPublicationStatus.Published)
            .ToListAsync(ct);

    public async Task<SocialDashboardSummaryDto> GetDashboardSummaryAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var todayStart = utcNow.Date;
        var weekStart = todayStart.AddDays(-7);
        var monthStart = todayStart.AddDays(-30);

        var statusCounts = await _db.SocialPublications
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(SocialPublicationStatus status) => statusCounts.FirstOrDefault(s => s.Status == status)?.Count ?? 0;

        var publishedCount = CountOf(SocialPublicationStatus.Published);
        var failedCount = CountOf(SocialPublicationStatus.Failed);
        var totalTerminal = publishedCount + failedCount;

        var publicationsToday = await _db.SocialPublications.CountAsync(p => p.CreatedAt >= todayStart, ct);
        var publicationsThisWeek = await _db.SocialPublications.CountAsync(p => p.CreatedAt >= weekStart, ct);
        var publicationsThisMonth = await _db.SocialPublications.CountAsync(p => p.CreatedAt >= monthStart, ct);

        var byPlatform = await _db.SocialPublications
            .Where(p => p.Content != null)
            .GroupBy(p => p.Content!.Platform)
            .Select(g => new PlatformCountDto(
                g.Key,
                g.Count(p => p.Status == SocialPublicationStatus.Published),
                g.Count(p => p.Status == SocialPublicationStatus.Failed)))
            .ToListAsync(ct);

        var byProvince = await (
            from p in _db.SocialPublications
            join a in _db.SocialAccounts on p.SocialAccountId equals a.Id
            where p.Status == SocialPublicationStatus.Published
            group p by a.GovernorateId into g
            select new GovernorateCountDto(g.Key, g.Count()))
            .ToListAsync(ct);

        return new SocialDashboardSummaryDto(
            publicationsToday,
            publicationsThisWeek,
            publicationsThisMonth,
            publishedCount,
            failedCount,
            CountOf(SocialPublicationStatus.Queued),
            CountOf(SocialPublicationStatus.Retrying),
            CountOf(SocialPublicationStatus.Publishing),
            CountOf(SocialPublicationStatus.Cancelled),
            DeadLetterUnresolved: 0, // filled in by the handler, which owns ISocialPublicationDeadLetterRepository — kept out of this repository to avoid a cross-aggregate dependency here.
            SuccessRatePercent: totalTerminal == 0 ? 0 : Math.Round(100.0 * publishedCount / totalTerminal, 2),
            FailureRatePercent: totalTerminal == 0 ? 0 : Math.Round(100.0 * failedCount / totalTerminal, 2),
            byPlatform,
            byProvince);
    }

    public void Update(SocialPublication publication) => _db.SocialPublications.Update(publication);
}
