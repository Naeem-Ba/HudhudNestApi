using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;

namespace HudhudNestApi.Application.ShortStay.Commands.CompleteBooking;

public sealed record CompleteBookingCommand(Guid BookingId, Guid HostId) : IRequest<BookingDto>;

public sealed class CompleteBookingCommandHandler : IRequestHandler<CompleteBookingCommand, BookingDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;

    public CompleteBookingCommandHandler(IBookingRepository bookings, IUnitOfWork uow)
    {
        _bookings = bookings;
        _uow = uow;
    }

    public async Task<BookingDto> Handle(CompleteBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        var listing = booking.Unit!.RoomType.ShortStayListing;
        if (listing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can complete this booking.");

        booking.Complete();
        await _bookings.ReleaseRangeAsync(booking.Id, ct); // frees the row from the "active" set; historical Completed booking stays queryable via Booking itself
        await _uow.SaveChangesAsync(ct);

        return booking.ToDto(listing.Id, listing.Title, listing.CurrencyCode);
    }
}
