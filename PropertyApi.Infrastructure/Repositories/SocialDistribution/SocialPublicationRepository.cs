using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories.SocialDistribution;

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

    public Task<bool> ExistsActiveForPropertyAndAccountAsync(Guid propertyId, Guid socialAccountId, CancellationToken ct = default) =>
        _db.SocialPublications.AnyAsync(p =>
            p.PropertyId == propertyId &&
            p.SocialAccountId == socialAccountId &&
            p.DistributionRuleId != null &&
            p.Status != SocialPublicationStatus.Cancelled,
            ct);

    public void Update(SocialPublication publication) => _db.SocialPublications.Update(publication);
}
