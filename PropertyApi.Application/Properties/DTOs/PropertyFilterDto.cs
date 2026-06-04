using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Properties.DTOs;

/// <summary>
/// Filter/search parameters for property listings.
/// Supports global multi-country search from day 1.
/// </summary>
public sealed class PropertyFilterDto
{
    // Pagination
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;  // Default 20, max 100

    // Location filters
    public string? CountryCode { get; set; }   // ISO 3166-1 alpha-2
    public string? City { get; set; }
    public string? Region { get; set; }

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

    // Sort
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
}
