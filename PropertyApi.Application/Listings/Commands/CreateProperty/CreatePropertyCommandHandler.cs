using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Commands.CreateProperty;

/// <summary>
/// Handles CreatePropertyCommand.
/// Uses the Property.Create() factory method (enforces domain invariants),
/// then saves via UnitOfWork.
///
/// BUG FIX: Was an empty "internal class CreatePropertyCommandHandler {}" — non-functional.
/// </summary>
public sealed class CreatePropertyCommandHandler
    : IRequestHandler<CreatePropertyCommand, Guid>
{
    private readonly IPropertyRepository _repo;
    private readonly IUnitOfWork _uow;

    public CreatePropertyCommandHandler(IPropertyRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Guid> Handle(
        CreatePropertyCommand request,
        CancellationToken cancellationToken)
    {
        // Factory method — the ONLY correct way to create a Property.
        // Throws DomainException if invariants are violated.
        var property = Property.Create(
            title: request.Title,
            description: request.Description,
            ownerId: request.OwnerId,
            listingType: request.ListingType,
            countryCode: request.CountryCode,
            currencyCode: request.CurrencyCode);

        // Set optional fields (public setters — no invariants to enforce)
        property.Street = request.Street;
        property.City = request.City;
        property.Region = request.Region;
        property.PostalCode = request.PostalCode;
        property.Latitude = request.Latitude;
        property.Longitude = request.Longitude;
        property.ColdRent = request.ColdRent;
        property.WarmRent = request.WarmRent;
        property.PurchasePrice = request.PurchasePrice;
        property.Deposit = request.Deposit;
        property.AdditionalCosts = request.AdditionalCosts;
        property.Rooms = request.Rooms;
        property.Area = request.Area;
        property.Floor = request.Floor;
        property.HasBalcony = request.HasBalcony;
        property.HasElevator = request.HasElevator;
        property.HasParkingSpace = request.HasParkingSpace;
        property.HeatingType = request.HeatingType;
        property.AvailableFrom = request.AvailableFrom;

        // Associate amenities if provided
        if (request.AmenityIds is { Count: > 0 })
        {
            foreach (var amenityId in request.AmenityIds)
            {
                property.PropertyAmenities.Add(new PropertyAmenity
                {
                    PropertyId = property.Id,
                    AmenityId = amenityId
                });
            }
        }

        await _repo.AddAsync(property, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return property.Id;
    }
}
