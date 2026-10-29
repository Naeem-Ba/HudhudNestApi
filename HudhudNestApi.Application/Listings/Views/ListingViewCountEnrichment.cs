using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.Views;

/// <summary>
/// Fills the read-only <c>ViewCount</c> on listing DTOs. The totals live in their own table (see
/// ListingViewCounter), so a page of N listings costs exactly one extra indexed query, never N.
/// </summary>
public static class ListingViewCountEnrichment
{
    public static async Task ApplyViewCountsAsync(
        this IListingViewRepository views, IReadOnlyCollection<PropertyDto> properties, CancellationToken ct)
    {
        if (properties.Count == 0)
            return;

        var counts = await views.GetCountsAsync(ListingKind.Property, properties.Select(p => p.Id).ToList(), ct);
        foreach (var property in properties)
            property.ViewCount = counts.TryGetValue(property.Id, out var n) ? n : 0;
    }

    public static async Task<IReadOnlyList<ShortStayListingSummaryDto>> WithViewCountsAsync(
        this IListingViewRepository views, IReadOnlyList<ShortStayListingSummaryDto> summaries, CancellationToken ct)
    {
        if (summaries.Count == 0)
            return summaries;

        var counts = await views.GetCountsAsync(ListingKind.ShortStay, summaries.Select(s => s.Id).ToList(), ct);
        return summaries.Select(s => s with { ViewCount = counts.TryGetValue(s.Id, out var n) ? n : 0 }).ToList();
    }

    public static async Task<ShortStayListingDto> WithViewCountAsync(
        this IListingViewRepository views, ShortStayListingDto dto, CancellationToken ct)
    {
        var counts = await views.GetCountsAsync(ListingKind.ShortStay, new[] { dto.Id }, ct);
        return dto with { ViewCount = counts.TryGetValue(dto.Id, out var n) ? n : 0 };
    }
}
