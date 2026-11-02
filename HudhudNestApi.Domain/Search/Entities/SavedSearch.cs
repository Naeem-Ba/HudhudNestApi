using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Search.Enums;

namespace HudhudNestApi.Domain.Search.Entities;

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

    // Amenity filters (growth engine P3). Only "true" is stored: "no balcony required" is the same as no filter.
    public bool? HasBalcony { get; private set; }
    public bool? HasElevator { get; private set; }
    public bool? HasParkingSpace { get; private set; }

    /// <summary>Instant (one notification per listing) or a daily summary.</summary>
    public SavedSearchFrequency Frequency { get; private set; } = SavedSearchFrequency.Instant;

    /// <summary>A paused search is skipped by the matcher; nothing found while paused is announced later.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Delivery log: when the owner was last notified and how many listings were announced in total.</summary>
    public DateTime? LastNotifiedAt { get; private set; }
    public int NotifiedCount { get; private set; }

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
        decimal? maxArea,
        bool? hasBalcony = null,
        bool? hasElevator = null,
        bool? hasParkingSpace = null,
        SavedSearchFrequency frequency = SavedSearchFrequency.Instant)
    {
        return new SavedSearch
        {
            HasBalcony = OnlyTrue(hasBalcony),
            HasElevator = OnlyTrue(hasElevator),
            HasParkingSpace = OnlyTrue(hasParkingSpace),
            Frequency = frequency,
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

    /// <summary>
    /// Replaces the criteria. The cursor moves to <paramref name="nowUtc"/>: listings that were already on the
    /// market are not announced as "new" just because the owner widened the search.
    /// </summary>
    public void Update(
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
        decimal? maxArea,
        bool? hasBalcony,
        bool? hasElevator,
        bool? hasParkingSpace,
        SavedSearchFrequency frequency,
        DateTime nowUtc)
    {
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        CountryCode = countryCode;
        City = city;
        Region = region;
        GovernorateId = governorateId;
        DistrictId = districtId;
        NeighborhoodId = neighborhoodId;
        PropertyTypeId = propertyTypeId;
        ListingType = listingType;
        MinPrice = minPrice;
        MaxPrice = maxPrice;
        CurrencyCode = currencyCode;
        MinRooms = minRooms;
        MaxRooms = maxRooms;
        MinArea = minArea;
        MaxArea = maxArea;
        HasBalcony = OnlyTrue(hasBalcony);
        HasElevator = OnlyTrue(hasElevator);
        HasParkingSpace = OnlyTrue(hasParkingSpace);
        Frequency = frequency;
        LastMatchedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void Pause(DateTime nowUtc)
    {
        IsPaused = true;
        UpdatedAt = nowUtc;
    }

    /// <summary>Resuming starts from now: listings published while paused are not sent in one burst.</summary>
    public void Resume(DateTime nowUtc)
    {
        if (!IsPaused)
            return;
        IsPaused = false;
        LastMatchedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    /// <summary>A daily search may notify again 24 hours after its last notification.</summary>
    public bool IsDigestDue(DateTime nowUtc) =>
        Frequency != SavedSearchFrequency.Daily || LastNotifiedAt is null || nowUtc - LastNotifiedAt >= TimeSpan.FromHours(24);

    public void RecordDelivery(int listingCount, DateTime nowUtc)
    {
        if (listingCount <= 0)
            return;
        LastNotifiedAt = nowUtc;
        NotifiedCount += listingCount;
        UpdatedAt = nowUtc;
    }

    private static bool? OnlyTrue(bool? value) => value == true ? true : null;

    public void MarkMatched()
    {
        LastMatchedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Advances the "matched up to" point to the creation time of the last listing that was handled, never
    /// backwards. Listings created after it stay eligible for the next sweep, so a cap on how many are
    /// notified per sweep cannot drop the remaining ones.
    /// </summary>
    public void MarkMatchedUpTo(DateTime handledListingCreatedAtUtc)
    {
        if (LastMatchedAt is null || handledListingCreatedAtUtc > LastMatchedAt)
        {
            LastMatchedAt = handledListingCreatedAtUtc;
        }

        UpdatedAt = DateTime.UtcNow;
    }
}
