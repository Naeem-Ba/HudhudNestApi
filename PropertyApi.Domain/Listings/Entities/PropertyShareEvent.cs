using System.Text.RegularExpressions;
using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Domain.Listings.Entities;

/// <summary>
/// One "share button was used successfully" event for a public listing (Social Sharing &amp;
/// Distribution feature, section 11). Deliberately minimal — this is a privacy-conscious
/// analytics record, not an audit trail:
///
///  - No message content, no recipient, no social-media handle, no access token — none of
///    that is ever collected by the frontend in the first place (see SocialShareService).
///  - <see cref="UserId"/> is nullable: most visitors sharing a listing are not logged in, and
///    the feature must work for them too.
///  - No IP address. Abuse is bounded by rate limiting on the endpoint, not by recording who
///    called it.
///
/// This only ever represents a *successful* share (or a successful copy-link) — see
/// TrackPropertyShareEventCommandHandler and PropertiesController.TrackShareEvent for where
/// that success is established before this is created. A cancelled or failed share attempt
/// (e.g. the user dismissed the native share sheet) never reaches here.
///
/// UTM / Attribution (Phase 2 — see docs/social-sharing.md §"UTM Attribution"): <see
/// cref="UtmSource"/>/<see cref="UtmMedium"/>/<see cref="UtmCampaign"/>/<see cref="UtmContent"/>
/// mirror the query parameters embedded in the attributed link the frontend actually opened —
/// they let a report answer "how many Facebook shares of property X turned into anything" instead
/// of just "how many shares happened". All four are optional and independently sanitized: a
/// client sending a malformed value for one field must never lose the rest of the event, so an
/// invalid field is silently normalized to <c>null</c> rather than rejecting the whole request
/// (tracking must never fail the user-visible share — see PropertyShareTrackingService on the
/// frontend, which never awaits this call either).
/// </summary>
public sealed class PropertyShareEvent : BaseEntity
{
    /// <summary>
    /// UTM field values are constrained to this charset and length so they stay usable as
    /// report/analytics dimensions (section 4 of the feature spec: "ثابتة، قابلة للتحليل، لا
    /// تحتوي على مسافات أو أحرف غير مناسبة"). Anything else is dropped, not stored partially.
    /// </summary>
    private const int MaxUtmFieldLength = 60;
    private static readonly Regex UtmFieldPattern = new("^[a-zA-Z0-9_.-]{1,60}$", RegexOptions.Compiled);

    public Guid PropertyId { get; private set; }
    public Guid? UserId { get; private set; }
    public SharePlatform Platform { get; private set; }

    public string? UtmSource { get; private set; }
    public string? UtmMedium { get; private set; }
    public string? UtmCampaign { get; private set; }
    public string? UtmContent { get; private set; }

    private PropertyShareEvent() { }

    public static PropertyShareEvent Create(
        Guid propertyId,
        SharePlatform platform,
        Guid? userId = null,
        string? utmSource = null,
        string? utmMedium = null,
        string? utmCampaign = null,
        string? utmContent = null)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");

        if (!Enum.IsDefined(platform))
            throw new DomainException("Unknown share platform.");

        return new PropertyShareEvent
        {
            PropertyId = propertyId,
            Platform = platform,
            UserId = userId,
            UtmSource = SanitizeUtmField(utmSource),
            UtmMedium = SanitizeUtmField(utmMedium),
            UtmCampaign = SanitizeUtmField(utmCampaign),
            UtmContent = SanitizeUtmField(utmContent)
        };
    }

    /// <summary>
    /// Trims, then either returns the value unchanged or null — never a truncated/partial
    /// value, since a silently-shortened UTM value would misreport as a different, real
    /// campaign/content value in downstream analytics. Never throws: an untrusted client value
    /// that fails validation simply isn't recorded for that one field.
    /// </summary>
    internal static string? SanitizeUtmField(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxUtmFieldLength)
            return null;

        return UtmFieldPattern.IsMatch(trimmed) ? trimmed : null;
    }
}
