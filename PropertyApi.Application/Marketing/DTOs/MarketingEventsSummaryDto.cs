namespace PropertyApi.Application.Marketing.DTOs;

/// <summary>
/// Conversion-funnel summary for the admin dashboard ("متابعة معدلات التحويل"). Counts are
/// raw event counts, not unique visitors — <c>MarketingEvent</c> has no identity/cookie to
/// deduplicate by design (see its class doc comment on privacy).
/// </summary>
public sealed record MarketingEventsSummaryDto(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyDictionary<string, int> CountsByEventType);
