using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Listings.DTOs;

public sealed class PropertyDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Location
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // Structured location (Phase-0 follow-up). BUG FIX: these existed on the
    // Property entity but were never mapped into this DTO, so every response
    // silently dropped them — the property-form edit page could never restore
    // a previously-saved governorate/district/neighborhood/property type.
    public int? GovernorateId { get; set; }
    public int? DistrictId { get; set; }
    public string? DistrictText { get; set; }
    public int? NeighborhoodId { get; set; }
    public string? NeighborhoodText { get; set; }
    public int? PropertyTypeId { get; set; }

    // BUG FIX: same issue for the "still available?" staleness feature — the
    // frontend banner reads this field, but it was never in the response.
    public DateTime? LastConfirmedAvailableAt { get; set; }

    // Listing
    public string ListingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    // Pricing
    public decimal? ColdRent { get; set; }
    public decimal? WarmRent { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? Deposit { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    // Details
    public int? Rooms { get; set; }
    public decimal? Area { get; set; }
    public int? Floor { get; set; }

    // Features
    public bool HasBalcony { get; set; }
    public bool HasElevator { get; set; }
    public bool HasParkingSpace { get; set; }

    // Ownership
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;

    // Publishing
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Related
    public string? MainImageUrl { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public List<AmenityDto> Amenities { get; set; } = new();
}

public sealed class AmenityDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? IconName { get; set; }
}

