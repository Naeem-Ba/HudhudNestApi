using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.Queries.GetSocialPerformance;

/// <summary>
/// Builds the Phase 14 report purely by aggregating <c>PropertyShareEvent</c>/
/// <c>PropertyAttributionEvent</c> rows (spec §10: "Source → Property → Visit → View → Click →
/// Contact → Lead") — never Publication counts, and never a vanity metric like "publications
/// created" mistaken for a real visit (spec: "لا تعتبر Publication Created زيارة").
/// "Contacts" merges every contact-affordance event type (generic ContactClick, PhoneClick,
/// WhatsAppClick, MessageSent) into one number, matching how the spec's example dashboard reports
/// a single "Contacts" tile per platform rather than one tile per contact channel.
/// </summary>
public sealed class GetSocialPerformanceQueryHandler : IRequestHandler<GetSocialPerformanceQuery, SocialPerformanceDto>
{
    private static readonly PropertyAttributionEventType[] ContactEventTypes =
    {
        PropertyAttributionEventType.ContactClick,
        PropertyAttributionEventType.PhoneClick,
        PropertyAttributionEventType.WhatsAppClick,
        PropertyAttributionEventType.MessageSent,
    };

    private readonly IPropertyShareEventRepository _shares;
    private readonly IPropertyAttributionEventRepository _attribution;

    public GetSocialPerformanceQueryHandler(IPropertyShareEventRepository shares, IPropertyAttributionEventRepository attribution)
    {
        _shares = shares;
        _attribution = attribution;
    }

    public async Task<SocialPerformanceDto> Handle(GetSocialPerformanceQuery request, CancellationToken ct)
    {
        var sharesTotal = await _shares.CountAsync(request.PropertyId, request.FromUtc, request.ToUtc, ct);
        var sharesByPlatform = await _shares.CountByUtmSourceAsync(request.PropertyId, request.FromUtc, request.ToUtc, ct);

        var visitsTotal = await _attribution.CountAsync(request.PropertyId, PropertyAttributionEventType.View, request.FromUtc, request.ToUtc, ct);
        var visitsByPlatform = await _attribution.CountByUtmSourceAsync(request.PropertyId, PropertyAttributionEventType.View, request.FromUtc, request.ToUtc, ct);

        var leadsTotal = await _attribution.CountAsync(request.PropertyId, PropertyAttributionEventType.VisitRequestCreated, request.FromUtc, request.ToUtc, ct);
        var leadsByPlatform = await _attribution.CountByUtmSourceAsync(request.PropertyId, PropertyAttributionEventType.VisitRequestCreated, request.FromUtc, request.ToUtc, ct);

        // UtmSource is legitimately null (direct/organic traffic with no UTM — see
        // UtmSourceCount), so the per-platform contact counts cannot live in a
        // Dictionary<string, int> keyed by UtmSource: Dictionary<TKey, TValue> requires a
        // non-null TKey, and coercing the null case into a sentinel string would either collide
        // with a real UtmSource value or silently misreport direct traffic. A flat list summed
        // per platform (matching how shares/visits/leads are already looked up below via
        // FirstOrDefault) keeps null a first-class key with no such risk.
        var contactsTotal = 0;
        var contactCounts = new List<UtmSourceCount>();
        foreach (var eventType in ContactEventTypes)
        {
            contactsTotal += await _attribution.CountAsync(request.PropertyId, eventType, request.FromUtc, request.ToUtc, ct);
            contactCounts.AddRange(await _attribution.CountByUtmSourceAsync(request.PropertyId, eventType, request.FromUtc, request.ToUtc, ct));
        }

        var platforms = new HashSet<string?>();
        foreach (var row in sharesByPlatform) platforms.Add(row.UtmSource);
        foreach (var row in visitsByPlatform) platforms.Add(row.UtmSource);
        foreach (var row in leadsByPlatform) platforms.Add(row.UtmSource);
        foreach (var row in contactCounts) platforms.Add(row.UtmSource);

        var byPlatform = platforms
            .Select(platform =>
            {
                var visits = visitsByPlatform.FirstOrDefault(r => r.UtmSource == platform)?.Count ?? 0;
                var leads = leadsByPlatform.FirstOrDefault(r => r.UtmSource == platform)?.Count ?? 0;
                var contacts = contactCounts.Where(r => r.UtmSource == platform).Sum(r => r.Count);
                return new SocialPerformanceByPlatformDto(
                    platform,
                    sharesByPlatform.FirstOrDefault(r => r.UtmSource == platform)?.Count ?? 0,
                    visits,
                    contacts,
                    leads,
                    ConversionRate(leads, visits));
            })
            .OrderByDescending(p => p.Visits)
            .ToList();

        return new SocialPerformanceDto(
            sharesTotal, visitsTotal, contactsTotal, leadsTotal, ConversionRate(leadsTotal, visitsTotal), byPlatform);
    }

    private static double ConversionRate(int leads, int visits) =>
        visits == 0 ? 0 : Math.Round(100.0 * leads / visits, 2);
}
