using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Domain.Marketing.Entities;

/// <summary>
/// One conversion-funnel event on a marketing surface (see MarketingEventType for the
/// covered events). Deliberately carries no IP address and no fingerprinting data —
/// <see cref="SessionId"/> is an opaque, client-generated identifier the frontend creates
/// itself (e.g. a random value kept in sessionStorage), not a cookie or device fingerprint,
/// so this table can reconstruct a funnel ("how many who saw the page also submitted the
/// form") without identifying a person.
/// </summary>
public sealed class MarketingEvent : BaseEntity
{
    public MarketingEventType EventType { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string? Campaign { get; private set; }
    public string? SessionId { get; private set; }
    public string? Path { get; private set; }
    public Guid? LeadId { get; private set; }
    public Guid? OfferId { get; private set; }

    private MarketingEvent() { }

    public static MarketingEvent Create(
        MarketingEventType eventType,
        string source,
        string? campaign = null,
        string? sessionId = null,
        string? path = null,
        Guid? leadId = null,
        Guid? offerId = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException("مصدر الحدث مطلوب.");

        return new MarketingEvent
        {
            EventType = eventType,
            Source = source.Trim(),
            Campaign = string.IsNullOrWhiteSpace(campaign) ? null : campaign.Trim(),
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim(),
            Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            LeadId = leadId,
            OfferId = offerId
        };
    }
}
