using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Listings.Interfaces;

/// <summary>
/// Throws when the owner (or their agency, pooled) has no room left under the plan's active-listing limit.
/// One implementation for every path that adds a listing to the active count: creating one, and
/// bringing an expired one back with a paid extension (QA Phase 8, P8-D05).
/// </summary>
public interface IListingQuotaGuard
{
    /// <exception cref="HudhudNestApi.Application.Common.Exceptions.ConflictException">
    /// The active count has reached the limit, or the limit cannot be resolved.
    /// </exception>
    Task EnsureCapacityAsync(UserAccount ownerAccount, CancellationToken ct = default);
}
