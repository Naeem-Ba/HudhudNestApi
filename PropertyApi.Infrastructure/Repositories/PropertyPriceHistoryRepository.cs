using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

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
}
