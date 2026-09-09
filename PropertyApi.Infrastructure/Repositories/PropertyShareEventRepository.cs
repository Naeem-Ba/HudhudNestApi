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
}
