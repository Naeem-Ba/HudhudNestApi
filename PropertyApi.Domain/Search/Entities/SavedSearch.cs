using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Domain.Search.Entities;

/// <summary>
/// A user's saved search — mirrors the filterable fields on PropertyFilterDto.
/// SavedSearchMatchHostedService periodically re-runs each saved search's filter
/// (via PropertyRepository.ApplyFilter — the exact same rules GetPagedAsync uses,
/// so results here never drift from what a manual search would return) and
/// notifies the owner about newly published matches created since LastMatchedAt.
/// </summary>
public class SavedSearch : BaseEntity
{
    public Guid UserId { get; private set; }

    /// <summary>Optional user-facing label, e.g. "2BR apartments in Mezzeh under $500".</summary>
    public string? Name { get; private set; }

    // -- Filter criteria (mirrors PropertyFilterDto) -------------
    public string? CountryCode { get; private set; }
    public string? City { get; private set; }
    public string? Region { get; private set; }
    public int? GovernorateId { get; private set; }
    public int? DistrictId { get; private set; }
    public int? NeighborhoodId { get; private set; }
    public int? PropertyTypeId { get; private set; }
    public ListingType? ListingType { get; private set; }
    public decimal? MinPrice { get; private set; }
    public decimal? MaxPrice { get; private set; }
    public string? CurrencyCode { get; private set; }
    public int? MinRooms { get; private set; }
    public int? MaxRooms { get; private set; }
    public decimal? MinArea { get; private set; }
    public decimal? MaxArea { get; private set; }

    /// <summary>
    /// Last time SavedSearchMatchHostedService checked this search and (if any)
    /// notified the owner. Null means it has never run for this search yet —
    /// the first run only looks at properties published from CreatedAt onward,
    /// so the owner is never flooded with every pre-existing match at once.
    /// </summary>
    public DateTime? LastMatchedAt { get; private set; }

    private SavedSearch() { }

    public static SavedSearch Create(
        Guid userId,
        string? name,
        string? countryCode,
        string? city,
        string? region,
        int? governorateId,
        int? districtId,
        int? neighborhoodId,
        int? propertyTypeId,
        ListingType? listingType,
        decimal? minPrice,
        decimal? maxPrice,
        string? currencyCode,
        int? minRooms,
        int? maxRooms,
        decimal? minArea,
        decimal? maxArea)
    {
        return new SavedSearch
        {
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            CountryCode = countryCode,
            City = city,
            Region = region,
            GovernorateId = governorateId,
            DistrictId = districtId,
            NeighborhoodId = neighborhoodId,
            PropertyTypeId = propertyTypeId,
            ListingType = listingType,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            CurrencyCode = currencyCode,
            MinRooms = minRooms,
            MaxRooms = maxRooms,
            MinArea = minArea,
            MaxArea = maxArea
        };
    }

    public void MarkMatched()
    {
        LastMatchedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
