using HudhudNestApi.Application.Favorites.DTOs;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Favorites.Interfaces;

public interface IFavoriteRepository
{
    Task<IReadOnlyList<FavoriteDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId, Guid propertyId, CancellationToken ct = default);
    Task<bool> PropertyExistsAsync(Guid propertyId, CancellationToken ct = default);
    Task<Favorite?> GetAsync(Guid userId, Guid propertyId, CancellationToken ct = default);
    void Add(Favorite favorite);
    void Remove(Favorite favorite);
}

