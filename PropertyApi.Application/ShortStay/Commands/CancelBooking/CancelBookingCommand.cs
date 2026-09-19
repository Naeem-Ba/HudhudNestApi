using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Mapping;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.ShortStay.Commands.CancelBooking;

public sealed record CancelBookingCommand(Guid BookingId, Guid GuestId, string? Reason = null) : IRequest<BookingDto>;

public sealed class CancelBookingCommandHandler : IRequestHandler<CancelBookingCommand, BookingDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;

    public CancelBookingCommandHandler(IBookingRepository bookings, IUnitOfWork uow, INotificationService notifications)
    {
        _bookings = bookings;
        _uow = uow;
        _notifications = notifications;
    }

    public async Task<BookingDto> Handle(CancelBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        var listing = booking.Unit!.RoomType.ShortStayListing;

        // Booking.Cancel already enforces GuestId == actorId and rejects terminal/checked-in states.
        booking.Cancel(request.GuestId, request.Reason);
        await _bookings.ReleaseRangeAsync(booking.Id, ct);
        await _uow.SaveChangesAsync(ct);

        await _notifications.NotifyShortStayBookingUpdateAsync(
            listing.OwnerId, booking.Id, listing.Title,
            NotificationType.ShortStayBookingCancelled,
            $"ألغى الضيف حجزه لـ '{listing.Title}'.", ct);

        return booking.ToDto(listing.Id, listing.Title, listing.CurrencyCode);
    }
}
