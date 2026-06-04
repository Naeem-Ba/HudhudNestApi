using MediatR;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Listings.Commands.CreateProperty;

/// <summary>
/// Command to create a new property listing.
/// Returns the new property's Guid ID on success.
///
/// CHANGE: Added ": IRequest&lt;Guid&gt;" — without this MediatR cannot route it.
/// </summary>
public sealed record CreatePropertyCommand(
    Guid OwnerId,
    string Title,
    string Description,
    ListingType ListingType,
    string Street,
    string City,
    string? Region,
    string CountryCode,
    string? PostalCode,
    decimal? Latitude,
    decimal? Longitude,
    decimal? ColdRent,
    decimal? WarmRent,
    decimal? PurchasePrice,
    decimal? Deposit,
    decimal? AdditionalCosts,
    string CurrencyCode,
    int? Rooms,
    decimal? Area,
    int? Floor,
    bool HasBalcony,
    bool HasElevator,
    bool HasParkingSpace,
    HeatingType HeatingType,
    DateTime? AvailableFrom,
    List<Guid>? AmenityIds
) : IRequest<Guid>;
