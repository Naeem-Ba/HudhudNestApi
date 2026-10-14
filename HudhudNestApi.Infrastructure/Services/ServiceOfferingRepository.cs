using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

public sealed class ServiceOfferingRepository : IServiceOfferingRepository
{
    private readonly AppDbContext _db;
    public ServiceOfferingRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ServiceOffering offering, CancellationToken ct = default)
        => await _db.ServiceOfferings.AddAsync(offering, ct);

    public async Task<ServiceOffering?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ServiceOfferings
            .Include(o => o.ServiceProvider)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<ServiceOffering>> GetActiveByCategoryAsync(
        ServiceCategory category, int? governorateId = null, CancellationToken ct = default)
        => await Bookable(governorateId)
            .Where(o => o.Category == category)
            .Include(o => o.ServiceProvider)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<ServiceCategory, int>> CountActiveByCategoryAsync(
        int? governorateId = null, CancellationToken ct = default)
        => await Bookable(governorateId)
            .GroupBy(o => o.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Category, x => x.Count, ct);

    /// <summary>
    /// The one definition of "the public can book this": the offering is active, its provider is
    /// Active (not Suspended/Revoked) and its account still exists, the offering's category is
    /// still approved for that provider, and the provider covers the governorate when one is
    /// given (an empty coverage list predates approvals and is not a restriction).
    /// </summary>
    private IQueryable<ServiceOffering> Bookable(int? governorateId)
    {
        var query = _db.ServiceOfferings
            .Where(o => o.IsActive)
            .Where(o => o.ServiceProvider!.Status == ServiceProviderStatus.Active)
            .Where(o => o.ServiceProvider!.ApprovedCategories.Contains(o.Category.ToString()))
            .Where(o => _db.UserAccounts.Any(u => u.Id == o.ServiceProvider!.UserId && !u.IsDeleted));

        if (governorateId is { } id)
        {
            query = query.Where(o =>
                o.ServiceProvider!.CoverageGovernorateIds.Length == 0 ||
                o.ServiceProvider!.CoverageGovernorateIds.Contains(id));
        }

        return query;
    }

    public async Task<IReadOnlyList<ServiceOffering>> GetByProviderIdAsync(
        Guid providerId, CancellationToken ct = default)
        => await _db.ServiceOfferings
            .Include(o => o.ServiceProvider)
            .Where(o => o.ServiceProviderId == providerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);
}
