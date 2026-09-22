using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class PropertyAttributionEventRepository : IPropertyAttributionEventRepository
{
    private readonly AppDbContext _db;

    public PropertyAttributionEventRepository(AppDbContext db)
        => _db = db;

    public void Add(PropertyAttributionEvent attributionEvent)
    {
        _db.PropertyAttributionEvents.Add(attributionEvent);
    }

    public Task<int> CountAsync(Guid? propertyId, PropertyAttributionEventType? eventType, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default) =>
        Filter(propertyId, eventType, fromUtc, toUtc).CountAsync(ct);

    public async Task<IReadOnlyList<UtmSourceCount>> CountByUtmSourceAsync(
        Guid? propertyId, PropertyAttributionEventType? eventType, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default) =>
        await Filter(propertyId, eventType, fromUtc, toUtc)
            .GroupBy(e => e.UtmSource)
            .Select(g => new UtmSourceCount(g.Key, g.Count()))
            .ToListAsync(ct);

    private IQueryable<PropertyAttributionEvent> Filter(Guid? propertyId, PropertyAttributionEventType? eventType, DateTime? fromUtc, DateTime? toUtc)
    {
        var query = _db.PropertyAttributionEvents.AsNoTracking().AsQueryable();

        if (propertyId is not null)
            query = query.Where(e => e.PropertyId == propertyId);

        if (eventType is not null)
            query = query.Where(e => e.EventType == eventType);

        if (fromUtc is not null)
            query = query.Where(e => e.CreatedAt >= fromUtc);

        if (toUtc is not null)
            query = query.Where(e => e.CreatedAt <= toUtc);

        return query;
    }
}
