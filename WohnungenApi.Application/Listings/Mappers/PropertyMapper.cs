// NEW FILE: Extracts MapToDto() from GetPropertyByIdQueryHandler.
//
// WHY: Having MapToDto() as an internal static method on a query handler
//      and calling it from another handler creates coupling between handlers.
//      A dedicated mapper is the correct pattern.
//
// USAGE:
//   using WohnungenApi.Application.Listings.Mappers;
//   var dto = PropertyMapper.ToDto(property);
//
// MIGRATION: Update GetPropertyByIdQueryHandler and GetPropertiesListQueryHandler
//            to call PropertyMapper.ToDto() instead of GetPropertyByIdQueryHandler.MapToDto()

using WohnungenApi.Application.Listings.DTOs;
using WohnungenApi.Domain.Listings.Entities;

namespace WohnungenApi.Application.Listings.Mappers;

public static class PropertyMapper
{
    public static PropertyDto ToDto(Property p) => new()
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
        Floor = p.Floor,

        // Features
        HasBalcony = p.HasBalcony,
        HasElevator = p.HasElevator,
        HasParkingSpace = p.HasParkingSpace,

        // Ownership
        OwnerId = p.OwnerId,
        OwnerName = p.Owner is not null
            ? $"{p.Owner.FirstName} {p.Owner.LastName}".Trim()
            : string.Empty,

        // Publishing
        IsPublished = p.IsPublished,
        PublishedAt = p.PublishedAt,
        ExpiresAt = p.ExpiresAt,

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