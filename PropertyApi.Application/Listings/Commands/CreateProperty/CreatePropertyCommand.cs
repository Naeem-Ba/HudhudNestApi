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

    // Structured location (tech-debt cleanup — see Phase-0 notes). Optional so
    // legacy clients that still only send free-text City/Region keep working.
    int? GovernorateId,
    int? DistrictId,
    // Manual fallback when DistrictId has no matching seeded row for the
    // chosen governorate — see Property.DistrictText.
    string? DistrictText,
    int? NeighborhoodId,
    // Manual fallback when NeighborhoodId has no matching seeded row for the
    // chosen district — see Property.NeighborhoodText.
    string? NeighborhoodText,
    int? PropertyTypeId,
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

