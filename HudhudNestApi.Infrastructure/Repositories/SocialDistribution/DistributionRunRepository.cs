using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories.SocialDistribution;

public sealed class DistributionRunRepository : IDistributionRunRepository
{
    private readonly AppDbContext _db;

    public DistributionRunRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(DistributionRun run, CancellationToken ct = default) =>
        await _db.DistributionRuns.AddAsync(run, ct);

    public Task<DistributionRun?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.DistributionRuns.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<PagedResult<DistributionRun>> GetByPropertyIdAsync(Guid propertyId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.DistributionRuns.AsNoTracking().Where(r => r.PropertyId == propertyId);

        var totalCount = await query.CountAsync(ct);
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        return new PagedResult<DistributionRun>
        {
            Items = items,
            TotalCount = totalCount,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    public async Task<IReadOnlyList<Guid>> GetPublishedPropertyIdsWithoutRunAsync(
        DateTime publishedSinceUtc, DateTime publishedBeforeUtc, int take, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Soft-deleted listings are already excluded by Property's global query filter.
        return await _db.Properties
            .AsNoTracking()
            .Where(property =>
                property.IsPublished &&
                property.Status == PropertyStatus.Available &&
                property.PublishedAt != null &&
                property.PublishedAt >= publishedSinceUtc &&
                property.PublishedAt <= publishedBeforeUtc &&
                (property.ExpiresAt == null || property.ExpiresAt > now) &&
                !_db.DistributionRuns.Any(run => run.PropertyId == property.Id && run.PublicationsCreatedCount > 0))
            .OrderBy(property => property.PublishedAt)
            .Select(property => property.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);
    }

    public void Update(DistributionRun run) => _db.DistributionRuns.Update(run);
}
