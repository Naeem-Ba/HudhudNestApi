using HudhudNestApi.Application.Notifications.Localization;
using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Reviews.Services;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;

namespace HudhudNestApi.Application.ShortStay.Commands.AddShortStayReview;

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
    private readonly IReviewTargetPolicy _target;

    public AddShortStayReviewCommandHandler(
        IBookingRepository bookings,
        IShortStayReviewRepository reviews,
        IUnitOfWork uow,
        INotificationService notifications,
        IReviewTargetPolicy target)
    {
        _bookings = bookings;
        _reviews = reviews;
        _uow = uow;
        _notifications = notifications;
        _target = target;
    }

    public async Task<ShortStayReviewDto> Handle(AddShortStayReviewCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdWithListingIncludingDeletedAsync(request.BookingId, ct)
            ?? throw new NotFoundException($"Booking {request.BookingId} was not found.");

        if (booking.GuestId != request.ReviewerId)
            throw new ForbiddenException("Only the guest who made this booking can review it.");

        if (booking.Status != BookingStatus.Completed)
            throw new DomainException("لا يمكن تقييم إقامة لم تكتمل بعد.");

        // After the stay, not just after the host pressed "complete": the check-out day must have come.
        if (booking.CheckOut > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new DomainException("يمكن تقييم الإقامة بعد موعد المغادرة فقط.");

        if (await _bookings.HasReviewAsync(booking.Id, ct))
            throw new DomainException("لقد قيّمت هذا الحجز مسبقاً.");

        var listing = booking.Unit!.RoomType.ShortStayListing;

        // Owner decision 2026-10-18: only a host whose plan is in force can receive a new review.
        await _target.EnsureReviewableAsync(listing.OwnerId, ct);

        var review = ShortStayReview.Create(booking.Id, request.ReviewerId, listing.Id, request.Rating, request.Comment);

        await _reviews.AddAsync(review, ct);
        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyShortStayBookingUpdateAsync(
                listing.OwnerId, booking.Id, listing.Title,
                NotificationType.ShortStayReviewAdded,
                NotificationText.Of(NotificationKeys.ShortStayReviewAddedDetail, ("title", listing.Title)), ct);
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
