using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
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

    public void Update(DistributionRun run) => _db.DistributionRuns.Update(run);
}
