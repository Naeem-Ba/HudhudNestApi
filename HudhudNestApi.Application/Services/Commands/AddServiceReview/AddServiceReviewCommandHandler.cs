using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Services.DTOs;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Application.Services.Mapping;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Commands.AddServiceReview;

/// <summary>
/// A "verified service review": can only exist once ServiceRequest.Status is Completed and the
/// reviewer is that request's own requester — mirrors AddReviewCommandHandler's completed-visit
/// gate, but checked against ServiceRequest.Status directly rather than a separate repository
/// query, since that state already lives on the aggregate this handler already loads.
/// </summary>
public sealed class AddServiceReviewCommandHandler
    : IRequestHandler<AddServiceReviewCommand, ServiceReviewDto>
{
    private readonly IServiceRequestRepository _requests;
    private readonly IServiceReviewRepository _reviews;
    private readonly IUserAccountRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<AddServiceReviewCommandHandler> _logger;

    public AddServiceReviewCommandHandler(
        IServiceRequestRepository requests,
        IServiceReviewRepository reviews,
        IUserAccountRepository users,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<AddServiceReviewCommandHandler> logger)
    {
        _requests = requests;
        _reviews = reviews;
        _users = users;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ServiceReviewDto> Handle(AddServiceReviewCommand request, CancellationToken ct)
    {
        var serviceRequest = await _requests.GetByIdAsync(request.ServiceRequestId, ct)
            ?? throw new NotFoundException("طلب الخدمة غير موجود.");

        if (serviceRequest.RequesterId != request.ReviewerId)
            throw new ForbiddenException("لا يمكنك تقييم طلب خدمة لم تُقدّمه أنت.");

        if (serviceRequest.Status != ServiceRequestStatus.Completed)
        {
            throw new DomainException(
                "يمكن تقييم الخدمة فقط بعد إتمامها. تأكد من اكتمال طلبك أولاً.");
        }

        // DB unique index on ServiceRequestId is the second line of defense.
        var alreadyReviewed = await _reviews.ExistsForRequestAsync(serviceRequest.Id, ct);
        if (alreadyReviewed)
            throw new DomainException("لقد قمت بتقييم هذا الطلب مسبقاً.");

        var now = DateTime.UtcNow;

        var review = ServiceReview.Create(
            serviceRequest.Id, serviceRequest.ServiceProviderId, request.ReviewerId,
            request.Rating, request.Comment, now);

        await _reviews.AddAsync(review, ct);

        serviceRequest.MarkReviewed(now);

        await _uow.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: serviceRequest.ServiceProvider?.UserId ?? Guid.Empty,
                propertyId: serviceRequest.PropertyId,
                propertyTitle: serviceRequest.Property?.Title ?? "-",
                type: NotificationType.ServiceReviewAdded,
                detail: $"تم إضافة تقييم جديد ({request.Rating}/5) لطلب الخدمة ({serviceRequest.RequestNumber}).",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send service-review-added notification. ServiceRequestId={ServiceRequestId}, ReviewId={ReviewId}",
                serviceRequest.Id, review.Id);
        }

        var reviewer = await _users.GetByIdAsync(request.ReviewerId, ct);
        var reviewerName = reviewer is null
            ? "—"
            : string.IsNullOrWhiteSpace(reviewer.DisplayName)
                ? $"{reviewer.FirstName} {reviewer.LastName}".Trim()
                : reviewer.DisplayName!;

        return ServiceMapper.ToDto(review, reviewerName);
    }
}
