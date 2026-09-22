using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.ShortStay.Commands.ApproveBooking;

public sealed record ApproveBookingCommand(Guid BookingId, Guid HostId, string? HostNote = null) : IRequest<BookingDto>;

/// <summary>
/// Approving a Pending booking is itself a race window (spec Scenario D/C): two overlapping
/// Pending requests can exist for the same unit, and approving one must both re-verify no
/// Reserved/CheckedIn conflict slipped in since the request was made, and auto-reject every
/// other Pending request that overlaps it. Both steps run inside the same advisory-lock-guarded
/// transaction used by CreateBookingCommandHandler.
/// </summary>
public sealed class ApproveBookingCommandHandler : IRequestHandler<ApproveBookingCommand, BookingDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<ApproveBookingCommandHandler> _logger;

    public ApproveBookingCommandHandler(
        IBookingRepository bookings,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<ApproveBookingCommandHandler> logger)
    {
        _bookings = bookings;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<BookingDto> Handle(ApproveBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        var listing = booking.Unit!.RoomType.ShortStayListing;
        if (listing.OwnerId != request.HostId)
            throw new ForbiddenException("Only the listing owner can approve this booking.");

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireAdvisoryLockAsync(ShortStayBookingLock.ForUnit(booking.UnitId), ct);

            var stillFree = !await _bookings.HasOverlappingReservationAsync(
                booking.UnitId, booking.CheckIn, booking.CheckOut, ct);

            if (!stillFree)
                throw new ConflictException("لم يعد بالإمكان الموافقة على هذا الحجز — تعارضت التواريخ مع حجز آخر تم تأكيده.");

            booking.Approve(request.HostNote);
            await _bookings.MarkRangeReservedAsync(booking.Id, ct);

            var others = await _bookings.GetOverlappingPendingBookingsAsync(
                booking.UnitId, booking.CheckIn, booking.CheckOut, booking.Id, ct);

            foreach (var other in others)
            {
                other.Reject("تم قبول حجز آخر يتعارض مع هذه التواريخ.");
                await _bookings.ReleaseRangeAsync(other.Id, ct);
            }

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);

            try
            {
                await _notifications.NotifyShortStayBookingUpdateAsync(
                    booking.GuestId, booking.Id, listing.Title,
                    NotificationType.ShortStayBookingApproved,
                    $"تمت الموافقة على حجزك لـ '{listing.Title}'.", ct);

                foreach (var other in others)
                {
                    await _notifications.NotifyShortStayBookingUpdateAsync(
                        other.GuestId, other.Id, listing.Title,
                        NotificationType.ShortStayBookingRejected,
                        $"للأسف تم قبول حجز آخر لنفس التواريخ في '{listing.Title}'.", ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send booking-approved notifications. BookingId={BookingId}", booking.Id);
            }

            return booking.ToDto(listing.Id, listing.Title, listing.CurrencyCode);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }
    }
}
