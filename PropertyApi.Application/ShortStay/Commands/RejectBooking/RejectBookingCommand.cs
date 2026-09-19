using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Application.ShortStay.Mapping;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.ShortStay.Commands.RejectBooking;

public sealed record RejectBookingCommand(Guid BookingId, Guid HostId, string? Reason = null) : IRequest<BookingDto>;

public sealed class RejectBookingCommandHandler : IRequestHandler<RejectBookingCommand, BookingDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;

    public RejectBookingCommandHandler(IBookingRepository bookings, IUnitOfWork uow, INotificationService notifications)
    {
        _bookings = bookings;
        _uow = uow;
        _notifications = notifications;
    }

    public async Task<BookingDto> Handle(RejectBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        var listing = booking.Unit!.RoomType.ShortStayListing;
        if (listing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can reject this booking.");

        booking.Reject(request.Reason);
        await _bookings.ReleaseRangeAsync(booking.Id, ct);
        await _uow.SaveChangesAsync(ct);

        await _notifications.NotifyShortStayBookingUpdateAsync(
            booking.GuestId, booking.Id, listing.Title,
            NotificationType.ShortStayBookingRejected,
            $"تم رفض حجزك لـ '{listing.Title}'.", ct);

        return booking.ToDto(listing.Id, listing.Title, listing.CurrencyCode);
    }
}
