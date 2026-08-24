namespace PropertyApi.Application.Listings.DTOs;

public sealed class GeoPropertySearchResultDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public string ListingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public decimal? ColdRent { get; set; }
    public decimal? WarmRent { get; set; }
    public decimal? PurchasePrice { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    public int? Rooms { get; set; }
    public decimal? Area { get; set; }

    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string? MainImageUrl { get; set; }

    /// <summary>
    /// Paid featured placement, so the map can badge the listing. Computed in SQL against
    /// now(), so a window that has already elapsed reads false even before the six-hourly
    /// sweep clears the column.
    ///
    /// Map results are NOT reordered by this. The nearest-candidates CTE truncates to the
    /// closest page of rows via the KNN operator before anything else runs, so promoting a
    /// featured listing would mean promoting only those that happened to fall inside that
    /// window — and "nearest first" would stop being true. Proximity is the contract of this
    /// endpoint; the badge is what featured buys here.
    /// </summary>
    public bool IsFeatured { get; set; }
    public DateTime? FeaturedUntil { get; set; }

    /// <summary>Distance from the requested point in meters.</summary>
    public double DistanceMeters { get; set; }

    /// <summary>Distance from the requested point in kilometers.</summary>
    public double DistanceKm => Math.Round(DistanceMeters / 1000d, 3);

    public DateTime CreatedAt { get; set; }
}

