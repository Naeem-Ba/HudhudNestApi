using HudhudNestApi.Application.Notifications.DTOs;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Notifications.Interfaces;

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
        Guid? relatedEntityId = null,
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
    /// Valuation Stage 5 (24h SLA): tells the requester their valuation inquiry's 24h window
    /// closed without a Final Valuation. <paramref name="hadPreliminaryEstimate"/>
    /// distinguishes the two paths the inquiry could have been on when it expired — true if
    /// the Fast Path already produced a preliminary estimate (Status was MatchedFromListings)
    /// before time ran out, false if it never got that far (Pending) or was still waiting on
    /// office responses (AwaitingOfficeResponses) — so the message can tell the requester
    /// which is true rather than a single generic "expired". Never called for an anonymous
    /// inquiry (RequesterId null) — see ValuationSlaEnforcementService. Called by
    /// ValuationInquiryExpiryHostedService.
    /// </summary>
    Task NotifyValuationInquiryExpiredAsync(
        Guid recipientId,
        Guid inquiryId,
        bool hadPreliminaryEstimate,
        CancellationToken ct = default);

    /// <summary>
    /// Valuation Stage 5 (24h SLA): tells the agency owner one of their office's invitations
    /// went unanswered until the inquiry's SLA window closed (or the inquiry was otherwise
    /// completed/expired without their response ever being needed). RecipientId is the
    /// agency's OwnerUserId — an invitation targets an Agency, and this codebase has no
    /// concept of notifying an Agency directly (see AgencyInvitation's own notifications,
    /// which resolve to Agency.OwnerUserId the same way). Called by
    /// ValuationInquiryExpiryHostedService.
    /// </summary>
    Task NotifyValuationOfficeInvitationExpiredAsync(
        Guid recipientId,
        Guid invitationId,
        Guid inquiryId,
        CancellationToken ct = default);

    /// <summary>
    /// Remediation H2 — tells the requester their valuation inquiry reached Completed because
    /// every invited office has now responded, before the 24h SLA window closes. Never called
    /// for an anonymous inquiry (RequesterId null). Called by SubmitOfficeResponseCommandHandler
    /// and retried by ValuationSlaEnforcementService.
    /// </summary>
    Task NotifyValuationResultReadyAsync(
        Guid recipientId,
        Guid inquiryId,
        CancellationToken ct = default);

    /// <summary>
    /// Remediation M4 — warns the requester their valuation inquiry has been open for 18h
    /// without a result, before the 24h SLA window closes. Never called for an anonymous
    /// inquiry (RequesterId null). Called by ValuationInquiryExpiryHostedService via
    /// ValuationSlaEnforcementService, at most once per inquiry.
    /// </summary>
    Task NotifyValuationInquiryReminderSoonAsync(
        Guid recipientId,
        Guid inquiryId,
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

