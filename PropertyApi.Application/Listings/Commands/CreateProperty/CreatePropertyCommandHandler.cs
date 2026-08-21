using MediatR;
using Microsoft.Extensions.Logging;
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
    private readonly ILocationSuggestionService _locationSuggestions;
    private readonly ILogger<CreatePropertyCommandHandler> _logger;

    public CreatePropertyCommandHandler(
        IPropertyRepository repo,
        IUnitOfWork uow,
        ILocationSuggestionService locationSuggestions,
        ILogger<CreatePropertyCommandHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _locationSuggestions = locationSuggestions;
        _logger = logger;
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
        property.GovernorateId = request.GovernorateId;
        property.DistrictId = request.DistrictId;
        property.DistrictText = request.DistrictText;
        property.NeighborhoodId = request.NeighborhoodId;
        property.NeighborhoodText = request.NeighborhoodText;
        property.PropertyTypeId = request.PropertyTypeId;
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

        // Best-effort — a manually-typed district/neighborhood name becomes a
        // suggestion for admin review (see LocationSuggestion's doc comment).
        // Deliberately never allowed to fail the actual listing save: this
        // runs after the property is already committed, and any exception
        // here is swallowed (with a log) rather than surfaced to the user.
        await TrySubmitLocationSuggestionsAsync(property, cancellationToken);

        return property.Id;
    }

    private async Task TrySubmitLocationSuggestionsAsync(Property property, CancellationToken ct)
    {
        try
        {
            if (property.DistrictId is null && !string.IsNullOrWhiteSpace(property.DistrictText) && property.GovernorateId.HasValue)
            {
                await _locationSuggestions.SubmitDistrictSuggestionAsync(
                    property.GovernorateId.Value, property.DistrictText, property.OwnerId, property.Id, ct);
            }

            if (property.NeighborhoodId is null && !string.IsNullOrWhiteSpace(property.NeighborhoodText) && property.DistrictId.HasValue)
            {
                await _locationSuggestions.SubmitNeighborhoodSuggestionAsync(
                    property.DistrictId.Value, property.NeighborhoodText, property.OwnerId, property.Id, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to submit location suggestion for property {PropertyId} — listing was still saved successfully.",
                property.Id);
        }
    }
}

