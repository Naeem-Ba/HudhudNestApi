using FluentValidation;
using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.ShortStay;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;

namespace HudhudNestApi.Application.ShortStay.Commands.ManageUnitAvailability;

// The host decides when a unit is available (owner decision 2026-10-18): days or periods can be marked as
// booked/unavailable, and a unit can be switched off for now. UnitRangeStatus.Blocked existed but nothing
// created or honoured it before.

/// <summary>Blocks [From, To) on a unit. To is exclusive, the same convention as a booking's check-out.</summary>
public sealed record AddUnitBlockCommand(Guid UnitId, Guid HostId, DateOnly From, DateOnly To) : IRequest<UnitBlockDto>;

public sealed class AddUnitBlockCommandValidator : AbstractValidator<AddUnitBlockCommand>
{
    public AddUnitBlockCommandValidator()
    {
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.To).GreaterThan(x => x.From).WithMessage("To must be after From.");

        RuleFor(x => x.From)
            .Must(from => from >= DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1))
            .WithMessage("A block cannot start in the past.")
            .Must(from => from <= DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(ShortStayBookingPolicy.MaxBlockAdvanceDays))
            .WithMessage($"A block cannot start more than {ShortStayBookingPolicy.MaxBlockAdvanceDays} days ahead.");

        RuleFor(x => x)
            .Must(x => x.To.DayNumber - x.From.DayNumber <= ShortStayBookingPolicy.MaxBlockNights)
            .WithMessage($"A block cannot cover more than {ShortStayBookingPolicy.MaxBlockNights} nights.")
            .WithName("To");
    }
}

public sealed class AddUnitBlockCommandHandler : IRequestHandler<AddUnitBlockCommand, UnitBlockDto>
{
    private readonly IAccommodationUnitRepository _units;
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;

    public AddUnitBlockCommandHandler(IAccommodationUnitRepository units, IBookingRepository bookings, IUnitOfWork uow)
    {
        _units = units;
        _bookings = bookings;
        _uow = uow;
    }

    public async Task<UnitBlockDto> Handle(AddUnitBlockCommand request, CancellationToken ct)
    {
        var unit = await _units.GetByIdWithListingAsync(request.UnitId, ct)
            ?? throw new NotFoundException($"Unit {request.UnitId} was not found.");

        if (unit.RoomType.ShortStayListing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can block dates.");

        await _uow.BeginTransactionAsync(ct);
        try
        {
            // Same lock as CreateBooking/ApproveBooking, so a booking being made right now and this block cannot both win.
            await _uow.AcquireAdvisoryLockAsync(ShortStayBookingLock.ForUnit(unit.Id), ct);

            if (await _bookings.HasOverlappingReservationAsync(unit.Id, request.From, request.To, ct))
                throw new ConflictException(
                    "هذه الفترة تتقاطع مع حجز مؤكَّد أو فترة محجوبة مسبقاً. أنهِ الحجز أو احذف الحجب القديم أولاً.");

            var existing = await _bookings.GetBlocksForUnitAsync(unit.Id, DateOnly.FromDateTime(DateTime.UtcNow.Date), ct);
            if (existing.Count >= ShortStayBookingPolicy.MaxBlocksPerUnit)
                throw new ConflictException($"لا يمكن إضافة أكثر من {ShortStayBookingPolicy.MaxBlocksPerUnit} فترة محجوبة لكل وحدة.");

            var block = UnitBookingRange.CreateBlock(unit.Id, request.From, request.To);
            await _bookings.AddBookingRangeAsync(block, ct);
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);

            return new UnitBlockDto(block.Id, block.UnitId, block.CheckIn, block.CheckOut);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }
    }
}

public sealed record RemoveUnitBlockCommand(Guid UnitId, Guid BlockId, Guid HostId) : IRequest<bool>;

public sealed class RemoveUnitBlockCommandHandler : IRequestHandler<RemoveUnitBlockCommand, bool>
{
    private readonly IAccommodationUnitRepository _units;
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;

    public RemoveUnitBlockCommandHandler(IAccommodationUnitRepository units, IBookingRepository bookings, IUnitOfWork uow)
    {
        _units = units;
        _bookings = bookings;
        _uow = uow;
    }

    public async Task<bool> Handle(RemoveUnitBlockCommand request, CancellationToken ct)
    {
        var unit = await _units.GetByIdWithListingAsync(request.UnitId, ct)
            ?? throw new NotFoundException($"Unit {request.UnitId} was not found.");

        if (unit.RoomType.ShortStayListing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can remove a block.");

        var block = await _bookings.GetBlockAsync(unit.Id, request.BlockId, ct)
            ?? throw new NotFoundException($"Block {request.BlockId} was not found.");

        // Released, not removed: ranges are soft-deleted rows and nothing filters them, while every
        // availability check already ignores Released.
        block.Status = UnitRangeStatus.Released;
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record GetUnitBlocksQuery(Guid UnitId, Guid HostId) : IRequest<IReadOnlyList<UnitBlockDto>>;

public sealed class GetUnitBlocksQueryHandler : IRequestHandler<GetUnitBlocksQuery, IReadOnlyList<UnitBlockDto>>
{
    private readonly IAccommodationUnitRepository _units;
    private readonly IBookingRepository _bookings;

    public GetUnitBlocksQueryHandler(IAccommodationUnitRepository units, IBookingRepository bookings)
    {
        _units = units;
        _bookings = bookings;
    }

    public async Task<IReadOnlyList<UnitBlockDto>> Handle(GetUnitBlocksQuery request, CancellationToken ct)
    {
        var unit = await _units.GetByIdWithListingAsync(request.UnitId, ct)
            ?? throw new NotFoundException($"Unit {request.UnitId} was not found.");

        if (unit.RoomType.ShortStayListing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can see the blocks.");

        var blocks = await _bookings.GetBlocksForUnitAsync(unit.Id, DateOnly.FromDateTime(DateTime.UtcNow.Date), ct);
        return blocks.Select(b => new UnitBlockDto(b.Id, b.UnitId, b.CheckIn, b.CheckOut)).ToList();
    }
}

/// <summary>"Not available for now": switches a unit off (or on) without touching bookings it already has.</summary>
public sealed record SetUnitActiveCommand(Guid UnitId, Guid HostId, bool IsActive) : IRequest<AccommodationUnitDto>;

public sealed class SetUnitActiveCommandHandler : IRequestHandler<SetUnitActiveCommand, AccommodationUnitDto>
{
    private readonly IAccommodationUnitRepository _units;
    private readonly IUnitOfWork _uow;

    public SetUnitActiveCommandHandler(IAccommodationUnitRepository units, IUnitOfWork uow)
    {
        _units = units;
        _uow = uow;
    }

    public async Task<AccommodationUnitDto> Handle(SetUnitActiveCommand request, CancellationToken ct)
    {
        var unit = await _units.GetByIdWithListingAsync(request.UnitId, ct)
            ?? throw new NotFoundException($"Unit {request.UnitId} was not found.");

        if (unit.RoomType.ShortStayListing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can change a unit's availability.");

        unit.IsActive = request.IsActive;
        await _uow.SaveChangesAsync(ct);
        return new AccommodationUnitDto(unit.Id, unit.Label, unit.IsActive);
    }
}
