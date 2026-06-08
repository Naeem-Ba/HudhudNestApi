using MediatR;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Queries.GetPropertyById;

/// <summary>
/// BUG FIX: Was an empty "internal class GetPropertyByIdQueryHandler {}" — completely missing.
/// </summary>
public sealed class GetPropertyByIdQueryHandler
    : IRequestHandler<GetPropertyByIdQuery, PropertyDto?>
{
    private readonly IPropertyRepository _repo;

    public GetPropertyByIdQueryHandler(IPropertyRepository repo)
        => _repo = repo;

    public async Task<PropertyDto?> Handle(
        GetPropertyByIdQuery request,
        CancellationToken cancellationToken)
    {
        var property = await _repo.GetPublishedByIdWithDetailsAsync(request.Id, cancellationToken);
        return property is null ? null : MapToDto(property);
    }

    /// <summary>
    /// Manual mapping Property ? PropertyDto.
    /// Internal so GetPropertiesListQueryHandler can reuse it.
    /// </summary>
    internal static PropertyDto MapToDto(Property p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Description = p.Description,
        Street = p.Street,
        City = p.City,
        Region = p.Region,
        CountryCode = p.CountryCode,
        PostalCode = p.PostalCode,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        ListingType = p.ListingType.ToString(),
        Status = p.Status.ToString(),
        ColdRent = p.ColdRent,
        WarmRent = p.WarmRent,
        PurchasePrice = p.PurchasePrice,
        Deposit = p.Deposit,
        CurrencyCode = p.CurrencyCode,
        Rooms = p.Rooms,
        Area = p.Area,
        Floor = p.Floor,
        HasBalcony = p.HasBalcony,
        HasElevator = p.HasElevator,
        HasParkingSpace = p.HasParkingSpace,
        OwnerId = p.OwnerId,
        OwnerName = p.Owner is not null
            ? $"{p.Owner.FirstName} {p.Owner.LastName}".Trim()
            : string.Empty,
        IsPublished = p.IsPublished,
        PublishedAt = p.PublishedAt,
        ExpiresAt = p.ExpiresAt,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        MainImageUrl = p.Images.FirstOrDefault(i => i.IsMain)?.Url,
        ImageUrls = p.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Url)
            .ToList(),
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
