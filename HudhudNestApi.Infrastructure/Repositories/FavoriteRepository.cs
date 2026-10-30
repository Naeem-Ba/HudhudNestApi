using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Favorites.DTOs;
using HudhudNestApi.Application.Favorites.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class FavoriteRepository : IFavoriteRepository
{
    private readonly AppDbContext _db;

    public FavoriteRepository(AppDbContext db)
        => _db = db;

    public async Task<IReadOnlyList<FavoriteDto>> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        return await _db.Favorites
            .AsNoTracking()
            .Where(favorite => favorite.UserId == userId)
            // A listing unpublished after it was saved must not keep showing its title, price and photo.
            .Where(favorite => favorite.Property!.IsPublished || favorite.Property.OwnerId == userId)
            .Include(favorite => favorite.Property)
                .ThenInclude(property => property!.Images.Where(image => image.IsMain))
            .Select(favorite => new FavoriteDto(
                favorite.PropertyId,
                favorite.CreatedAt,
                new FavoritePropertyDto(
                    favorite.Property!.Title,
                    favorite.Property.City,
                    favorite.Property.CountryCode,
                    favorite.Property.ColdRent,
                    favorite.Property.PurchasePrice,
                    favorite.Property.Images
                        .Where(image => image.IsMain)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    favorite.Property.WarmRent,
                    favorite.Property.CurrencyCode)))
            .ToListAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid userId, Guid propertyId, CancellationToken ct = default)
    {
        return _db.Favorites
            .AnyAsync(favorite => favorite.UserId == userId && favorite.PropertyId == propertyId, ct);
    }

    public Task<bool> PropertyVisibleToAsync(Guid propertyId, Guid userId, CancellationToken ct = default)
    {
        return _db.Properties.AnyAsync(
            property => property.Id == propertyId && (property.IsPublished || property.OwnerId == userId), ct);
    }

    public Task<Favorite?> GetAsync(Guid userId, Guid propertyId, CancellationToken ct = default)
    {
        return _db.Favorites
            .FirstOrDefaultAsync(favorite => favorite.UserId == userId && favorite.PropertyId == propertyId, ct);
    }

    public void Add(Favorite favorite)
    {
        _db.Favorites.Add(favorite);
    }

    public void Remove(Favorite favorite)
    {
        _db.Favorites.Remove(favorite);
    }
}
