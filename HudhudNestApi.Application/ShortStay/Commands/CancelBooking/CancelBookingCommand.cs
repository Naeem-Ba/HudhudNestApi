using HudhudNestApi.Application.Notifications.Localization;
using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.ShortStay.Commands.CancelBooking;

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
        var booking = await _bookings.GetByIdWithListingIncludingDeletedAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        // A non-guest gets 403 like every other booking command; the state rules (terminal and
        // checked-in bookings) stay in Booking.Cancel.
        if (booking.GuestId != request.GuestId)
            throw new ForbiddenException("Only the guest can cancel this booking.");

        var listing = booking.Unit!.RoomType.ShortStayListing;

        booking.Cancel(request.GuestId, request.Reason);
        await _bookings.ReleaseRangeAsync(booking.Id, ct);
        await _uow.SaveChangesAsync(ct);

        await _notifications.NotifyShortStayBookingUpdateAsync(
            listing.OwnerId, booking.Id, listing.Title,
            NotificationType.ShortStayBookingCancelled,
            NotificationText.Of(NotificationKeys.ShortStayCancelledByGuestDetail, ("title", listing.Title)), ct);

        return booking.ToDto(listing.Id, listing.Title, listing.CurrencyCode);
    }
}
