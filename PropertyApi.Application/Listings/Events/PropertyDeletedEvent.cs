using MediatR;

namespace PropertyApi.Application.Listings.Events;

/// <summary>
/// Raised whenever <c>DeletePropertyCommandHandler</c> soft-deletes a Property (Phase 1 audit
/// F-10). Distinct from <see cref="PropertyStatusChangedEvent"/> on purpose: a delete does not
/// change <c>Property.Status</c> at all (a live, <c>Available</c> listing can be deleted directly,
/// with no intervening status transition <see cref="Domain.SocialDistribution.Policies.
/// SocialPublicationLifecyclePolicy"/> could ever key off), so reusing that event and inventing a
/// fake "new status" would misreport history. Same isolation guarantee as every other Listings
/// event: Listings publishes this and knows nothing about who (if anyone) listens.
///
/// Not raised by <c>ListingExpiryHostedService</c>'s own grace-period deletion
/// (<c>MarkExpiredListingDeletedBySystem</c>) — that listing already transitioned to
/// <see cref="Domain.Enums.PropertyStatus.Expired"/> earlier, which already told
/// SocialDistribution (via <see cref="PropertyStatusChangedEvent"/>) to delete any live post; this
/// event exists for the case that skips that step entirely — an owner/admin deleting a still-live
/// listing directly.
/// </summary>
public sealed record PropertyDeletedEvent(
    Guid PropertyId,
    Domain.Enums.PropertyStatus StatusAtDeletion,
    DateTime DeletedAtUtc,
    Guid? DeletedByUserId) : INotification;
