using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Domain.Listings.Entities;

/// <summary>
/// One post-share funnel event for a public listing — a page view or a contact/lead action —
/// carrying whatever UTM/Attribution context the visitor's current attribution window has (see
/// docs/social-sharing.md §"UTM Attribution"). Sibling to <see cref="PropertyShareEvent"/>
/// (which records the share itself, not what happens after); kept as a separate table rather
/// than widening PropertyShareEvent, matching this codebase's existing convention of one
/// purpose-built event table per bounded concern (see also
/// PropertyApi.Domain.Marketing.Entities.MarketingEvent, a structurally similar but unrelated
/// table for the landing-page funnel — reusing it here would mix two different domains behind
/// one FK-less "Source" string).
///
/// Same privacy posture as PropertyShareEvent: no message content, no IP address, <see
/// cref="UserId"/> optional. UTM fields reuse PropertyShareEvent's sanitizer so the same
/// charset/length rules apply everywhere.
/// </summary>
public sealed class PropertyAttributionEvent : BaseEntity
{
    public Guid PropertyId { get; private set; }
    public Guid? UserId { get; private set; }
    public PropertyAttributionEventType EventType { get; private set; }

    public string? UtmSource { get; private set; }
    public string? UtmMedium { get; private set; }
    public string? UtmCampaign { get; private set; }
    public string? UtmContent { get; private set; }

    private PropertyAttributionEvent() { }

    public static PropertyAttributionEvent Create(
        Guid propertyId,
        PropertyAttributionEventType eventType,
        Guid? userId = null,
        string? utmSource = null,
        string? utmMedium = null,
        string? utmCampaign = null,
        string? utmContent = null)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");

        if (!Enum.IsDefined(eventType))
            throw new DomainException("Unknown attribution event type.");

        return new PropertyAttributionEvent
        {
            PropertyId = propertyId,
            EventType = eventType,
            UserId = userId,
            UtmSource = PropertyShareEvent.SanitizeUtmField(utmSource),
            UtmMedium = PropertyShareEvent.SanitizeUtmField(utmMedium),
            UtmCampaign = PropertyShareEvent.SanitizeUtmField(utmCampaign),
            UtmContent = PropertyShareEvent.SanitizeUtmField(utmContent)
        };
    }
}
