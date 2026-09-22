using MediatR;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.Commands.UpdateProperty;

/// <summary>
/// Partial-update command. All fields are nullable — only non-null values are applied.
/// This avoids the anti-pattern of accidentally overwriting fields with null.
///
/// BUG FIX: Was an empty "internal class UpdatePropertyCommand {}" — completely missing.
/// </summary>
public sealed record UpdatePropertyCommand(
    Guid PropertyId,
    Guid RequestingUserId,         // Authorization: must be owner (or admin)

    // Core
    string? Title,
    string? Description,

    // Address
    string? Street,
    string? City,
    string? Region,
    string? CountryCode,
    string? PostalCode,

    // Structured location (tech-debt cleanup — see Phase-0 notes)
    int? GovernorateId,
    int? DistrictId,
    string? DistrictText,
    int? NeighborhoodId,
    string? NeighborhoodText,
    int? PropertyTypeId,

    // Geo
    decimal? Latitude,
    decimal? Longitude,

    // Pricing
    decimal? ColdRent,
    decimal? WarmRent,
    decimal? PurchasePrice,
    decimal? Deposit,
    decimal? AdditionalCosts,
    string? CurrencyCode,

    // Physical
    int? Rooms,
    decimal? Area,
    AreaUnit? AreaUnit,
    int? Floor,
    int? TotalFloors,

    // Rental term (Rent only — see Property.RentalStartDate's doc comment)
    DateOnly? RentalStartDate,
    DateOnly? RentalEndDate,
    RentalDurationType? RentalDurationType,

    // Ownership document type ("OwnershipType" in the UI) + furnishing
    LegalStatusType? LegalStatus,
    FurnishingStatus? FurnishingStatus,

    // Features
    bool? HasBalcony,
    bool? HasElevator,
    bool? HasParkingSpace,
    HeatingType? HeatingType,

    // Classification
    PropertyStatus? Status,
    PropertyCondition? Condition,
    EnergyEfficiencyType? EnergyEfficiency,

    // Dates
    DateTime? AvailableFrom,
    DateTime? ExpiresAt,

    // Publishing — triggers domain methods Publish()/Unpublish()
    bool? IsPublished
) : IRequest<bool>;

