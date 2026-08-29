using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Mapping;

namespace PropertyApi.Application.ShortStay.Commands.CheckInBooking;

public sealed record CheckInBookingCommand(Guid BookingId, Guid HostId) : IRequest<BookingDto>;

public sealed class CheckInBookingCommandHandler : IRequestHandler<CheckInBookingCommand, BookingDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;

    public CheckInBookingCommandHandler(IBookingRepository bookings, IUnitOfWork uow)
    {
        _bookings = bookings;
        _uow = uow;
    }

    public async Task<BookingDto> Handle(CheckInBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        var listing = booking.Unit!.RoomType.ShortStayListing;
        if (listing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can check in this booking.");

        booking.CheckInGuest();
        await _bookings.MarkRangeCheckedInAsync(booking.Id, ct);
        await _uow.SaveChangesAsync(ct);

        return booking.ToDto(listing.Id, listing.Title);
    }
}
