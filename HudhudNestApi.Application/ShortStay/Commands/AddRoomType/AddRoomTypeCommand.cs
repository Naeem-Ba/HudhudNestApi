using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.ShortStay.Commands.AddRoomType;

/// <summary>
/// Lets a host expand a listing beyond its auto-created default RoomType — this is the
/// operation that turns a single-property listing into a hotel-style listing (spec §19/Scenario
/// F): "Hotel X: Standard x10 $80, Deluxe x5 $120, Suite x2 $200" is one listing with three
/// RoomTypes, each with N AccommodationUnits added via AddAccommodationUnit.
/// </summary>
public sealed record AddRoomTypeCommand(
    Guid ListingId,
    Guid OwnerId,
    string Name,
    decimal BasePricePerNight,
    int? Capacity) : IRequest<RoomTypeDto>;

public sealed class AddRoomTypeCommandHandler : IRequestHandler<AddRoomTypeCommand, RoomTypeDto>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IRoomTypeRepository _roomTypes;
    private readonly IUnitOfWork _uow;

    public AddRoomTypeCommandHandler(IShortStayListingRepository listings, IRoomTypeRepository roomTypes, IUnitOfWork uow)
    {
        _listings = listings;
        _roomTypes = roomTypes;
        _uow = uow;
    }

    public async Task<RoomTypeDto> Handle(AddRoomTypeCommand request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException($"Listing {request.ListingId} was not found.");

        if (listing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can add room types.");

        var roomType = new RoomType
        {
            ShortStayListingId = listing.Id,
            Name = request.Name.Trim(),
            BasePricePerNight = request.BasePricePerNight,
            Capacity = request.Capacity,
        };

        await _roomTypes.AddAsync(roomType, ct);
        await _uow.SaveChangesAsync(ct);

        return roomType.ToDto();
    }
}
