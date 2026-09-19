using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Mapping;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.ShortStay.Commands.ConfirmBooking;

public sealed record ConfirmBookingCommand(Guid BookingId, Guid HostId) : IRequest<BookingDto>;

public sealed class ConfirmBookingCommandHandler : IRequestHandler<ConfirmBookingCommand, BookingDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;

    public ConfirmBookingCommandHandler(IBookingRepository bookings, IUnitOfWork uow, INotificationService notifications)
    {
        _bookings = bookings;
        _uow = uow;
        _notifications = notifications;
    }

    public async Task<BookingDto> Handle(ConfirmBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        var listing = booking.Unit!.RoomType.ShortStayListing;
        if (listing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can confirm this booking.");

        booking.Confirm();
        await _uow.SaveChangesAsync(ct);

        await _notifications.NotifyShortStayBookingUpdateAsync(
            booking.GuestId, booking.Id, listing.Title,
            NotificationType.ShortStayBookingConfirmed,
            $"تم تأكيد حجزك لـ '{listing.Title}'.", ct);

        return booking.ToDto(listing.Id, listing.Title, listing.CurrencyCode);
    }
}
