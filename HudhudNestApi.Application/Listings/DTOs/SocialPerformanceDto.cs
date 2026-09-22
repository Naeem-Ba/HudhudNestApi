namespace HudhudNestApi.Application.Listings.DTOs;

/// <summary>
/// Phase 14 "Social Performance" report — every number here comes straight from
/// <c>PropertyShareEvent</c>/<c>PropertyAttributionEvent</c> rows (Phase 1/2), never estimated or
/// inferred (spec §10: "يجب أن تكون الأرقام مبنية على Events حقيقية"). Optionally scoped to one
/// property and/or a UTC date range by <c>GetSocialPerformanceQuery</c>.
/// </summary>
public sealed record SocialPerformanceDto(
    int Shares,
    int Visits,
    int Contacts,
    int Leads,
    double ConversionRatePercent,
    IReadOnlyList<SocialPerformanceByPlatformDto> ByPlatform);

/// <summary>One platform's row in the Phase 14 report — <see cref="Platform"/> is the raw UtmSource value (e.g. "facebook"), null for direct/organic traffic with no UTM.</summary>
public sealed record SocialPerformanceByPlatformDto(
    string? Platform,
    int Shares,
    int Visits,
    int Contacts,
    int Leads,
    double ConversionRatePercent);
