using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Interfaces;

public interface IPropertyRepository
{
    // Create
    Task AddAsync(Property property, CancellationToken ct = default);

    // Read
    Task<Property?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Property?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Property?> GetPublishedByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Property>> GetPagedAsync(PropertyFilterDto filter, CancellationToken ct = default);
    Task<IReadOnlyList<Property>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Counts how many of this owner's properties are currently marked Sold vs.
    /// Rented, across ALL of their listings (published or not — a listing is
    /// usually unpublished once a deal closes, but the deal still counts).
    /// Used by the public profile page's "X sold / Y rented through the platform"
    /// stat tiles.
    /// </summary>
    Task<(int SoldCount, int RentedCount)> GetDealCountsByOwnerAsync(
        Guid ownerId,
        CancellationToken ct = default);

    /// <summary>
    /// Counts this owner's listings that consume their plan quota.
    ///
    /// "Active" excludes deleted listings and excludes Expired ones: an expired listing
    /// sitting in its grace window must not block the owner from posting something new,
    /// or the quota would silently punish them for a listing they can no longer show.
    /// Unpublished drafts DO count — otherwise the limit is trivially bypassed by never
    /// pressing publish.
    /// </summary>
    Task<int> CountActiveListingsByOwnerAsync(
        Guid ownerId,
        CancellationToken ct = default);

    /// <summary>
    /// Advisory potential-duplicate lookup (Phase-0, Task 4). Returns published,
    /// non-deleted properties in the same neighborhood and listing type whose price
    /// and/or area fall within +/-maxTolerancePercent of the supplied values. Returns
    /// the widest reasonable candidate set (both same-owner and different-owner
    /// matches) — the caller (CheckPotentialDuplicatePropertyQueryHandler) applies the
    /// tighter same-owner-vs-different-owner tolerance split and never uses this to
    /// block anything.
    /// </summary>
    Task<IReadOnlyList<Property>> FindPotentialDuplicatesAsync(
        int neighborhoodId,
        ListingType listingType,
        decimal? price,
        decimal? area,
        decimal maxTolerancePercent,
        CancellationToken ct = default);

    // Update — repository just tracks, UnitOfWork saves
    void Update(Property property);

    // Delete — soft delete via MarkAsDeleted() domain method
    void Remove(Property property);
}

