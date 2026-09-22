using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.ShortStay.Commands.ConfirmBooking;

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
