using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Reviews.DTOs;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Domain.Reviews.Entities;

namespace PropertyApi.Application.Reviews.Commands.AddReview;

public sealed class AddReviewCommandHandler
    : IRequestHandler<AddReviewCommand, PropertyReviewDto>
{
    private readonly IPropertyReviewRepository _reviews;
    private readonly IPropertyReadRepository _properties;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;

    public AddReviewCommandHandler(
        IPropertyReviewRepository reviews,
        IPropertyReadRepository properties,
        IUnitOfWork uow,
        INotificationService notifications)
    {
        _reviews = reviews;
        _properties = properties;
        _uow = uow;
        _notifications = notifications;
    }

    public async Task<PropertyReviewDto> Handle(
        AddReviewCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct)
            ?? throw new NotFoundException("Ø§Ù„Ø¹Ù‚Ø§Ø± ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯.");

        if (property.OwnerId == request.ReviewerId)
            throw new DomainException("Ù„Ø§ ÙŠÙ…ÙƒÙ† Ù„Ù„Ù…Ø§Ù„Ùƒ ØªÙ‚ÙŠÙŠÙ… Ø¹Ù‚Ø§Ø±Ù‡.");

        var alreadyReviewed = await _reviews.HasUserReviewedAsync(
            request.PropertyId, request.ReviewerId, ct);
        if (alreadyReviewed)
            throw new DomainException("Ù„Ù‚Ø¯ Ù‚Ù…Øª Ø¨ØªÙ‚ÙŠÙŠÙ… Ù‡Ø°Ø§ Ø§Ù„Ø¹Ù‚Ø§Ø± Ù…Ø³Ø¨Ù‚Ù‹Ø§.");

        var review = PropertyReview.Create(
            request.PropertyId, request.ReviewerId,
            request.Rating, request.Comment);

        await _reviews.AddAsync(review, ct);
        await _uow.SaveChangesAsync(ct);

        // Notify property owner
        await _notifications.NotifyPropertyUpdateAsync(
            recipientId: property.OwnerId,
            propertyId: property.Id,
            propertyTitle: property.Title,
            type: NotificationType.ReviewAdded,
            detail: $"ØªÙ… Ø¥Ø¶Ø§ÙØ© ØªÙ‚ÙŠÙŠÙ… Ø¬Ø¯ÙŠØ¯ ({request.Rating}/5) Ù„Ø¹Ù‚Ø§Ø±Ùƒ.",
            ct: ct);

        return new PropertyReviewDto(
            Id: review.Id,
            PropertyId: review.PropertyId,
            ReviewerId: review.ReviewerId,
            ReviewerName: "â€”",   // resolved in query handler with navigation
            ReviewerImageUrl: null,
            Rating: review.Rating,
            Comment: review.Comment,
            CreatedAt: review.CreatedAt);
    }
}
