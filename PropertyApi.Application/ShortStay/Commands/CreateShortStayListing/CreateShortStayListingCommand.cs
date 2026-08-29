using MediatR;
using PropertyApi.Application.ShortStay.DTOs;

namespace PropertyApi.Application.ShortStay.Commands.CreateShortStayListing;

/// <summary>
/// Creates a listing together with one auto-generated default RoomType + AccommodationUnit,
/// so every listing is bookable immediately — a hotel host later calls AddRoomType/
/// AddAccommodationUnit to expand beyond this single default unit; single-property hosts never
/// need to know RoomType/Unit exist at all.
/// </summary>
public sealed record CreateShortStayListingCommand(
    Guid OwnerId,
    int AccommodationTypeId,
    string Title,
    string Description,
    int Capacity,
    int Bedrooms,
    int Bathrooms,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    decimal Latitude,
    decimal Longitude,
    decimal DefaultBasePricePerNight,
    Guid? PropertyId) : IRequest<ShortStayListingDto>;
