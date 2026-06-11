using PropertyApi.Application.Favorites.DTOs;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Favorites.Interfaces;

public interface IFavoriteRepository
{
    Task<IReadOnlyList<FavoriteDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId, Guid propertyId, CancellationToken ct = default);
    Task<bool> PropertyExistsAsync(Guid propertyId, CancellationToken ct = default);
    Task<Favorite?> GetAsync(Guid userId, Guid propertyId, CancellationToken ct = default);
    void Add(Favorite favorite);
    void Remove(Favorite favorite);
}
