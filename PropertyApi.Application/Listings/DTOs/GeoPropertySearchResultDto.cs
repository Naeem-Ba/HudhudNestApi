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

    /// <summary>Distance from the requested point in meters.</summary>
    public double DistanceMeters { get; set; }

    /// <summary>Distance from the requested point in kilometers.</summary>
    public double DistanceKm => Math.Round(DistanceMeters / 1000d, 3);

    public DateTime CreatedAt { get; set; }
}
