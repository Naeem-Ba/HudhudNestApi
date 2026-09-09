using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class PropertyShareEventRepository : IPropertyShareEventRepository
{
    private readonly AppDbContext _db;

    public PropertyShareEventRepository(AppDbContext db)
        => _db = db;

    public void Add(PropertyShareEvent shareEvent)
    {
        _db.PropertyShareEvents.Add(shareEvent);
    }

    public Task<int> CountAsync(Guid? propertyId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default) =>
        Filter(propertyId, fromUtc, toUtc).CountAsync(ct);

    public async Task<IReadOnlyList<UtmSourceCount>> CountByUtmSourceAsync(Guid? propertyId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default) =>
        await Filter(propertyId, fromUtc, toUtc)
            .GroupBy(e => e.UtmSource)
            .Select(g => new UtmSourceCount(g.Key, g.Count()))
            .ToListAsync(ct);

    private IQueryable<PropertyShareEvent> Filter(Guid? propertyId, DateTime? fromUtc, DateTime? toUtc)
    {
        var query = _db.PropertyShareEvents.AsNoTracking().AsQueryable();

        if (propertyId is not null)
            query = query.Where(e => e.PropertyId == propertyId);

        if (fromUtc is not null)
            query = query.Where(e => e.CreatedAt >= fromUtc);

        if (toUtc is not null)
            query = query.Where(e => e.CreatedAt <= toUtc);

        return query;
    }
}
