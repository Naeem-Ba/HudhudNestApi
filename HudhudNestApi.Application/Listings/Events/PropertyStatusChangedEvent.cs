using MediatR;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Listings.Events;

/// <summary>
/// Raised whenever <c>UpdatePropertyCommandHandler</c> actually changes a Property's
/// <see cref="PropertyStatus"/> (Phase 11 spec §7) — never for a no-op "update" that leaves the
/// status unchanged. Same pattern and same isolation guarantee as
/// <see cref="PropertyPublishedEvent"/>: Listings publishes this and knows nothing about who (if
/// anyone) listens; SocialDistribution's <c>PropertyStatusChangedDistributionHandler</c> is
/// today's only subscriber.
///
/// Carries only what spec §7 asks for — never the Property entity itself, never owner/contact
/// details.
/// </summary>
public sealed record PropertyStatusChangedEvent(
    Guid PropertyId,
    PropertyStatus PreviousStatus,
    PropertyStatus NewStatus,
    DateTime ChangedAtUtc) : INotification;
