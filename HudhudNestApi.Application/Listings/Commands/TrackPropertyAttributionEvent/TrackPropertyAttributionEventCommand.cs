using MediatR;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.Commands.TrackPropertyAttributionEvent;

/// <summary>
/// Records a post-share funnel event (a property view, or a contact/lead action) for a public
/// listing, together with whatever UTM/Attribution context the visitor's browser currently has.
/// <see cref="UserId"/> is null for anonymous visitors — most visitors browsing a shared link
/// aren't logged in.
/// </summary>
public sealed record TrackPropertyAttributionEventCommand(
    Guid PropertyId,
    PropertyAttributionEventType EventType,
    Guid? UserId,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? UtmContent) : IRequest<Guid>;
