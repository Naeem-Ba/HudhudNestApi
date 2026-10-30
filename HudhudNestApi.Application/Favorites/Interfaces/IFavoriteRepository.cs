using HudhudNestApi.Application.Favorites.DTOs;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Favorites.Interfaces;

public interface IFavoriteRepository
{
    Task<IReadOnlyList<FavoriteDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId, Guid propertyId, CancellationToken ct = default);
    /// <summary>
    /// True when the listing exists and the user may see it: it is published, or the user owns it.
    /// A draft of someone else must look as missing (QA Phase 12, P12-D04).
    /// </summary>
    Task<bool> PropertyVisibleToAsync(Guid propertyId, Guid userId, CancellationToken ct = default);
    Task<Favorite?> GetAsync(Guid userId, Guid propertyId, CancellationToken ct = default);
    void Add(Favorite favorite);
    void Remove(Favorite favorite);
}

