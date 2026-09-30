using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Properties.DTOs;

/// <summary>
/// Filter/search parameters for property listings.
/// Supports global multi-country search from day 1.
/// </summary>
public sealed class PropertyFilterDto
{
    // Pagination
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;  // Default 20, max 100

    // Free-text keyword search — matched against Title/Description (ILIKE, case-insensitive).
    // Independent of, and AND'ed with, every other filter below (Phase 3).
    public string? SearchTerm { get; set; }

    // Location filters
    public string? CountryCode { get; set; }   // ISO 3166-1 alpha-2
    public string? City { get; set; }
    public string? Region { get; set; }

    // Structured location filters (tech-debt cleanup — see Phase-0 notes).
    // Prefer these over the free-text City/Region filters above when available:
    // they match against the FK id instead of doing a substring/ILIKE scan.
    public int? GovernorateId { get; set; }
    public int? DistrictId { get; set; }
    public int? NeighborhoodId { get; set; }
    public int? PropertyTypeId { get; set; }

    // Type / Status
    public ListingType? ListingType { get; set; }
    public PropertyStatus? Status { get; set; }
    public PropertyCondition? Condition { get; set; }

    // Price range
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? CurrencyCode { get; set; }

    // Property details
    public int? MinRooms { get; set; }
    public int? MaxRooms { get; set; }
    public decimal? MinArea { get; set; }
    public decimal? MaxArea { get; set; }

    // Features
    public bool? HasBalcony { get; set; }
    public bool? HasElevator { get; set; }
    public bool? HasParkingSpace { get; set; }

    // Amenities (list of AmenityIds)
    public List<Guid>? AmenityIds { get; set; }

    // Owner filter
    public Guid? OwnerId { get; set; }

    // Agency filter — RELEASE-BLOCKERS-AR.md B-5. Same shape as OwnerId above: a public,
    // anonymous "this agency's other listings" filter, not an authorization boundary.
    public Guid? AgencyId { get; set; }

    // Sort
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
}

