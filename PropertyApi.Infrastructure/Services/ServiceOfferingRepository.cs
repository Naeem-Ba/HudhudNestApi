using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Domain.Services.Entities;
using PropertyApi.Domain.Services.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Services;

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
        ServiceCategory category, CancellationToken ct = default)
        => await _db.ServiceOfferings
            .Include(o => o.ServiceProvider)
            .Where(o => o.Category == category && o.IsActive)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceOffering>> GetByProviderIdAsync(
        Guid providerId, CancellationToken ct = default)
        => await _db.ServiceOfferings
            .Include(o => o.ServiceProvider)
            .Where(o => o.ServiceProviderId == providerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);
}
