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

    /// <summary>
    /// The governorates the caller is interested in, as one comma-separated list of at most
    /// <see cref="MaxInterestGovernorates"/> ids ("3,7"). The client builds it from the user's
    /// own profile area and their last searched areas; the server stores nothing about it.
    /// It only changes the order of the default (newest-first) list: featured ads whose
    /// governorate is in this set come before other featured ads. A single string so that the
    /// cache key is one stable value (see <see cref="ParseInterestGovernorateIds"/>).
    /// </summary>
    public string? InterestGovernorateIds { get; set; }

    public const int MaxInterestGovernorates = 3;

    /// <summary>
    /// The distinct, positive ids in <see cref="InterestGovernorateIds"/>, sorted ascending so
    /// that "7,3" and "3,7" are one set. Empty when the value is absent or malformed (the
    /// validator rejects malformed values before this is ever used for a query).
    /// </summary>
    public IReadOnlyList<int> ParseInterestGovernorateIds()
        => TryParseInterestGovernorateIds(InterestGovernorateIds, out var ids) ? ids : [];

    public static bool TryParseInterestGovernorateIds(string? raw, out IReadOnlyList<int> ids)
    {
        ids = [];
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var parsed = new SortedSet<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0)
                return false;
            parsed.Add(id);
        }

        if (parsed.Count > MaxInterestGovernorates)
            return false;

        ids = parsed.ToArray();
        return true;
    }

    // Sort
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
}

