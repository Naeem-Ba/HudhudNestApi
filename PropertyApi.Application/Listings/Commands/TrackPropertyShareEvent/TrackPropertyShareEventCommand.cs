using MediatR;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Application.Listings.Commands.TrackPropertyShareEvent;

/// <summary>
/// Records a successful property-share (or copy-link) event. <see cref="UserId"/> is null for
/// anonymous visitors — see PropertyShareEvent's doc comment for why that is intentional, not
/// an oversight. The four Utm* fields are optional UTM/Attribution parameters (Phase 2) carried
/// from the attributed share link the frontend actually opened — see
/// PropertyApi.Domain.Listings.Entities.PropertyShareEvent for how they're validated/sanitized.
/// </summary>
public sealed record TrackPropertyShareEventCommand(
    Guid PropertyId,
    SharePlatform Platform,
    Guid? UserId,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null,
    string? UtmContent = null) : IRequest<Guid>;
