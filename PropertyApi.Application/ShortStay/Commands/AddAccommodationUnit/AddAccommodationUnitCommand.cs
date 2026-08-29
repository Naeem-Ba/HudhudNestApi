using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Commands.AddAccommodationUnit;

public sealed record AddAccommodationUnitCommand(Guid RoomTypeId, Guid OwnerId, string Label) : IRequest<AccommodationUnitDto>;

public sealed class AddAccommodationUnitCommandHandler : IRequestHandler<AddAccommodationUnitCommand, AccommodationUnitDto>
{
    private readonly IRoomTypeRepository _roomTypes;
    private readonly IAccommodationUnitRepository _units;
    private readonly IUnitOfWork _uow;

    public AddAccommodationUnitCommandHandler(
        IRoomTypeRepository roomTypes, IAccommodationUnitRepository units, IUnitOfWork uow)
    {
        _roomTypes = roomTypes;
        _units = units;
        _uow = uow;
    }

    public async Task<AccommodationUnitDto> Handle(AddAccommodationUnitCommand request, CancellationToken ct)
    {
        var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct)
            ?? throw new NotFoundException($"RoomType {request.RoomTypeId} was not found.");

        if (roomType.ShortStayListing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can add units.");

        var unit = new AccommodationUnit { RoomTypeId = roomType.Id, Label = request.Label.Trim() };
        await _units.AddAsync(unit, ct);
        await _uow.SaveChangesAsync(ct);

        return new AccommodationUnitDto(unit.Id, unit.Label, unit.IsActive);
    }
}
