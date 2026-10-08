using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Reviews.Interfaces;

namespace HudhudNestApi.Application.Listings.Mappers;

/// <summary>
/// Adds the agency card (name, slug, logo, rating) to listing DTOs for the public list and
/// detail. One batch lookup per page, never one per listing. Only active, non-deleted agencies
/// are shown, matching the visibility of the public agency page; everything else stays null.
/// </summary>
public static class AgencyListingInfoEnricher
{
    public static async Task EnrichAsync(
        IReadOnlyCollection<PropertyDto> listings,
        IAgencyRepository agencies,
        IAgencyRatingRepository ratings,
        CancellationToken ct)
    {
        var agencyIds = listings
            .Where(l => l.AgencyId.HasValue)
            .Select(l => l.AgencyId!.Value)
            .Distinct()
            .ToList();

        if (agencyIds.Count == 0)
            return;

        var visible = (await agencies.GetByIdsAsync(agencyIds, ct))
            .Where(a => a is { IsDeleted: false, IsActive: true })
            .ToDictionary(a => a.Id);

        if (visible.Count == 0)
            return;

        var summaries = await ratings.GetOverallSummariesAsync(visible.Keys.ToList(), ct);

        foreach (var listing in listings)
        {
            if (listing.AgencyId is not { } id || !visible.TryGetValue(id, out var agency))
                continue;

            listing.AgencyName = agency.Name;
            listing.AgencySlug = agency.Slug;
            listing.AgencyLogoUrl = agency.LogoUrl;

            if (summaries.TryGetValue(id, out var summary) && summary.Count > 0)
            {
                listing.AgencyRatingAverage = Math.Round(summary.Overall, 1);
                listing.AgencyRatingCount = summary.Count;
            }
        }
    }
}
