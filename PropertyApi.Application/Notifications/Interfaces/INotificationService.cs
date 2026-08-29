using PropertyApi.Application.Notifications.DTOs;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Notifications.Interfaces;

public interface INotificationService
{
    Task NotifyNewMessageAsync(
        Guid recipientId,
        Guid senderId,
        string senderName,
        Guid messageId,
        Guid propertyId,
        CancellationToken ct = default);

    Task NotifyPropertyUpdateAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        NotificationType type,
        string detail,
        CancellationToken ct = default);

    /// <summary>
    /// Phase-0, Task 3 — notifies a user that a newly published property matched
    /// one of their saved searches. Called by SavedSearchMatchHostedService.
    /// </summary>
    Task NotifySavedSearchMatchAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        string savedSearchName,
        CancellationToken ct = default);

    /// <summary>
    /// Notifies a user that another user rated them (new UserRating). Called
    /// by RateUserCommandHandler after the rating is persisted.
    /// </summary>
    Task NotifyUserRatedAsync(
        Guid recipientId,
        Guid raterId,
        string raterName,
        double overallScore,
        CancellationToken ct = default);

    /// <summary>
    /// Warns the owner that a listing's publication window is about to close, and how many
    /// days are left. Called by ListingExpiryHostedService once per publication window.
    /// </summary>
    Task NotifyListingExpiringSoonAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        int daysRemaining,
        CancellationToken ct = default);

    /// <summary>
    /// Tells the owner a listing has expired, is no longer visible, and how many days remain
    /// before it is deleted. This is the message the paid-extension flow hangs off, so the
    /// grace window must be stated in it — an owner who does not know the deadline cannot act
    /// on it. Called by ListingExpiryHostedService.
    /// </summary>
    Task NotifyListingExpiredAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        int graceDaysRemaining,
        CancellationToken ct = default);

    /// <summary>
    /// B-2 (RELEASE-BLOCKERS-AR.md): tells a user an agency owner invited them to join.
    /// Called by CreateAgencyInvitationCommandHandler after the invitation is persisted.
    /// RelatedEntityId on the resulting notification is the invitation id, not the agency —
    /// the recipient acts on the invitation, and the agency behind it may still change name
    /// before they get to it.
    /// </summary>
    Task NotifyAgencyInvitationReceivedAsync(
        Guid recipientId,
        Guid invitationId,
        string agencyName,
        string inviterName,
        CancellationToken ct = default);

    /// <summary>
    /// Tells the agency owner their invitation was accepted or declined. Called by
    /// AcceptAgencyInvitationCommandHandler / DeclineAgencyInvitationCommandHandler.
    /// </summary>
    Task NotifyAgencyInvitationRespondedAsync(
        Guid recipientId,
        Guid invitationId,
        string targetUserName,
        bool accepted,
        CancellationToken ct = default);

    /// <summary>
    /// Generic short-stay booking-lifecycle notification. RelatedEntityId on the resulting
    /// notification is the bookingId — NOT PropertyId, since a short-stay listing is its own
    /// aggregate and is not necessarily backed by a Property row (see ShortStayListing.PropertyId,
    /// which is nullable). Called from ShortStay booking command handlers.
    /// </summary>
    Task NotifyShortStayBookingUpdateAsync(
        Guid recipientId,
        Guid bookingId,
        string listingTitle,
        NotificationType type,
        string detail,
        CancellationToken ct = default);

    Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);

    Task MarkAllAsReadAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Physically deletes one notification owned by the current user.
    /// Returns false if the notification does not exist or does not belong to the user.
    /// </summary>
    Task<bool> DeleteNotificationAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes one notification owned by the current user by setting IsDeleted/DeletedAt.
    /// Returns false if the notification does not exist or does not belong to the user.
    /// </summary>
    Task<bool> SoftDeleteNotificationAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);
}

