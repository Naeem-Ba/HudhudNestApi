// Property -> PropertyDto mapping.
//
// WHY IT LIVES HERE: it started life as an internal static method on a query handler, which
// other handlers then reached into — coupling handlers to each other, and letting a second
// copy drift out of sync with this one for months. A dedicated mapper is the correct pattern,
// and having exactly one is the point.
//
// USAGE:
//   using HudhudNestApi.Application.Listings.Mappers;
//   var dto = PropertyMapper.ToDto(property);            // clock = DateTime.UtcNow
//   var dto = PropertyMapper.ToDto(property, asOfUtc);   // fixed clock, for tests and pages


using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Listings.Mappers;

public static class PropertyMapper
{
    /// <summary>
    /// The single Property -> PropertyDto mapping. There used to be a second one
    /// (GetPropertyByIdQueryHandler.MapToDto) serving the public detail and list endpoints,
    /// and it silently fell seven fields behind this one — the structured-location fix landed
    /// here and never there. It has been deleted; every caller goes through this method now.
    ///
    /// asOfUtc is injectable so the featured window can be evaluated at a fixed instant in
    /// tests, and so a whole page of results is mapped against one clock reading.
    /// </summary>
    public static PropertyDto ToDto(Property p, DateTime? asOfUtc = null) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Description = p.Description,

        // Location
        Street = p.Street,
        City = p.City,
        Region = p.Region,
        CountryCode = p.CountryCode,
        PostalCode = p.PostalCode,
        Latitude = p.Latitude,
        Longitude = p.Longitude,

        // Structured location + freshness — see PropertyDto's doc comments
        // for why these were missing before this pass.
        GovernorateId = p.GovernorateId,
        DistrictId = p.DistrictId,
        DistrictText = p.DistrictText,
        NeighborhoodId = p.NeighborhoodId,
        NeighborhoodText = p.NeighborhoodText,
        PropertyTypeId = p.PropertyTypeId,
        LastConfirmedAvailableAt = p.LastConfirmedAvailableAt,

        // Listing
        ListingType = p.ListingType.ToString(),
        Status = p.Status.ToString(),

        // Pricing
        ColdRent = p.ColdRent,
        WarmRent = p.WarmRent,
        PurchasePrice = p.PurchasePrice,
        Deposit = p.Deposit,
        CurrencyCode = p.CurrencyCode,

        // Details
        Rooms = p.Rooms,
        Area = p.Area,
        AreaUnit = p.AreaUnit.ToString(),
        Floor = p.Floor,

        // Rental term (Rent only) + ownership/furnishing — see Property's
        // doc comments for why these live directly on the entity.
        RentalStartDate = p.RentalStartDate,
        RentalEndDate = p.RentalEndDate,
        RentalDurationType = p.RentalDurationType?.ToString(),
        LegalStatus = p.LegalStatus.ToString(),
        FurnishingStatus = p.FurnishingStatus.ToString(),

        // Features
        HasBalcony = p.HasBalcony,
        HasElevator = p.HasElevator,
        HasParkingSpace = p.HasParkingSpace,

        // Ownership
        OwnerId = p.OwnerId,
        OwnerName = p.Owner is not null
            ? $"{p.Owner.FirstName} {p.Owner.LastName}".Trim()
            : string.Empty,

        // See PropertyDto.AgencyId's doc comment (RELEASE-BLOCKERS-AR.md B-5).
        AgencyId = p.AgencyId,

        // Publishing
        IsPublished = p.IsPublished,
        PublishedAt = p.PublishedAt,
        ExpiresAt = p.ExpiresAt,

        // Paid featured placement — effective value, not the raw flag. See PropertyDto.
        IsFeatured = p.IsCurrentlyFeatured(asOfUtc ?? DateTime.UtcNow),
        FeaturedUntil = p.FeaturedUntil,

        // Timestamps
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,

        // Images
        MainImageUrl = p.Images.FirstOrDefault(i => i.IsMain)?.Url,
        ImageUrls = p.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Url)
            .ToList(),

        // Amenities
        Amenities = p.PropertyAmenities
            .Select(pa => new AmenityDto
            {
                Id = pa.AmenityId,
                Name = pa.Amenity?.Name ?? string.Empty,
                Category = pa.Amenity?.Category,
                IconName = pa.Amenity?.IconName
            })
            .ToList()
    };
}

