using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Listings.DTOs;

public sealed class GeoPropertySearchRequestDto
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    /// <summary>Search radius in kilometers. Default: 5 km.</summary>
    public decimal RadiusKm { get; set; } = 5m;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }

    public ListingType? ListingType { get; set; }
    public PropertyStatus? Status { get; set; }
    public PropertyCondition? Condition { get; set; }

    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? CurrencyCode { get; set; }

    public int? MinRooms { get; set; }
    public int? MaxRooms { get; set; }
    public decimal? MinArea { get; set; }
    public decimal? MaxArea { get; set; }

    public bool? HasBalcony { get; set; }
    public bool? HasElevator { get; set; }
    public bool? HasParkingSpace { get; set; }

    public Guid? OwnerId { get; set; }
}
