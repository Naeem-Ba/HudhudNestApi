using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Mapping;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Commands.CreateShortStayListing;

public sealed class CreateShortStayListingCommandHandler
    : IRequestHandler<CreateShortStayListingCommand, ShortStayListingDto>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IAccommodationTypeRepository _accommodationTypes;
    private readonly IUnitOfWork _uow;

    public CreateShortStayListingCommandHandler(
        IShortStayListingRepository listings,
        IAccommodationTypeRepository accommodationTypes,
        IUnitOfWork uow)
    {
        _listings = listings;
        _accommodationTypes = accommodationTypes;
        _uow = uow;
    }

    public async Task<ShortStayListingDto> Handle(CreateShortStayListingCommand request, CancellationToken ct)
    {
        var accommodationType = await _accommodationTypes.GetByIdAsync(request.AccommodationTypeId, ct)
            ?? throw new NotFoundException($"AccommodationType {request.AccommodationTypeId} was not found.");

        var listing = ShortStayListing.Create(
            request.OwnerId,
            request.AccommodationTypeId,
            request.Title,
            request.Description,
            request.Capacity,
            request.Bedrooms,
            request.Bathrooms,
            request.CheckInTime,
            request.CheckOutTime,
            request.Latitude,
            request.Longitude,
            request.PropertyId);

        var defaultRoomType = new RoomType
        {
            Name = "الوحدة الافتراضية",
            BasePricePerNight = request.DefaultBasePricePerNight,
        };
        defaultRoomType.Units.Add(new AccommodationUnit { Label = "الوحدة الوحيدة" });
        listing.RoomTypes.Add(defaultRoomType);

        await _listings.AddAsync(listing, ct);
        await _uow.SaveChangesAsync(ct);

        return listing.ToDto(accommodationType);
    }
}
