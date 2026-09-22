using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;

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
}
