using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;

namespace PropertyApi.Application.ShortStay.Commands.AddShortStayReview;

public sealed record AddShortStayReviewCommand(
    Guid BookingId,
    Guid ReviewerId,
    int Rating,
    string? Comment) : IRequest<ShortStayReviewDto>;

/// <summary>
/// Mirrors AddReviewCommandHandler's eligibility pattern for PropertyReview (mirrored
/// pattern, not a shared base class — the eligibility source differs: a completed Booking
/// here vs. a completed VisitRequest there): eligibility = reviewer is the booking's guest,
/// the booking reached Completed, and no review already exists for this booking.
/// </summary>
public sealed class AddShortStayReviewCommandHandler : IRequestHandler<AddShortStayReviewCommand, ShortStayReviewDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IShortStayReviewRepository _reviews;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;

    public AddShortStayReviewCommandHandler(
        IBookingRepository bookings,
        IShortStayReviewRepository reviews,
        IUnitOfWork uow,
        INotificationService notifications)
    {
        _bookings = bookings;
        _reviews = reviews;
        _uow = uow;
        _notifications = notifications;
    }

    public async Task<ShortStayReviewDto> Handle(AddShortStayReviewCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        if (booking.GuestId != request.ReviewerId)
            throw new ForbiddenException("Only the guest who made this booking can review it.");

        if (booking.Status != BookingStatus.Completed)
            throw new DomainException("لا يمكن تقييم إقامة لم تكتمل بعد.");

        if (await _bookings.HasReviewAsync(booking.Id, ct))
            throw new DomainException("لقد قيّمت هذا الحجز مسبقاً.");

        var listing = booking.Unit!.RoomType.ShortStayListing;
        var review = ShortStayReview.Create(booking.Id, request.ReviewerId, listing.Id, request.Rating, request.Comment);

        await _reviews.AddAsync(review, ct);
        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyShortStayBookingUpdateAsync(
                listing.OwnerId, booking.Id, listing.Title,
                NotificationType.ShortStayReviewAdded,
                $"حصل إعلانك '{listing.Title}' على تقييم جديد.", ct);
        }
        catch
        {
            // Notification failure must never fail a persisted review — same established
            // pattern as AddReviewCommandHandler/RequestVisitCommandHandler.
        }

        return new ShortStayReviewDto(review.Id, review.BookingId, review.ReviewerId, review.ShortStayListingId,
            review.Rating, review.Comment, review.CreatedAt);
    }
}
