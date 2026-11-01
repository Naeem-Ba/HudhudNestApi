using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class PropertyPriceHistoryRepository : IPropertyPriceHistoryRepository
{
    private readonly AppDbContext _db;

    public PropertyPriceHistoryRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(PropertyPriceHistory entry, CancellationToken ct = default)
    {
        await _db.PropertyPriceHistories.AddAsync(entry, ct);
    }

    public async Task<IReadOnlyList<PropertyPriceHistory>> GetLatestAsync(Guid propertyId, int take, CancellationToken ct = default)
    {
        return await _db.PropertyPriceHistories
            .AsNoTracking()
            .Where(h => h.PropertyId == propertyId)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Take(take)
            .ToListAsync(ct);
    }
}
