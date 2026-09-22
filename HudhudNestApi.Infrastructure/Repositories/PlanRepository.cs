using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class PlanRepository : IPlanRepository
{
    private readonly AppDbContext _db;

    public PlanRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Plan>> GetActiveAsync(CancellationToken ct = default)
        => await _db.Plans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync(ct);

    public async Task<Plan?> GetByTierAsync(string tier, CancellationToken ct = default)
    {
        var normalized = tier.Trim().ToLowerInvariant();

        return await _db.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Tier == normalized && p.IsActive, ct);
    }

    public async Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
}
