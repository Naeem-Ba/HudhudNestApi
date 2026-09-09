using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class PropertyAttributionEventRepository : IPropertyAttributionEventRepository
{
    private readonly AppDbContext _db;

    public PropertyAttributionEventRepository(AppDbContext db)
        => _db = db;

    public void Add(PropertyAttributionEvent attributionEvent)
    {
        _db.PropertyAttributionEvents.Add(attributionEvent);
    }
}
