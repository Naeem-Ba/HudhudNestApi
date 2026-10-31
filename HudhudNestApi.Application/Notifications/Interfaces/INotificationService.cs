using HudhudNestApi.Application.Notifications.DTOs;
using HudhudNestApi.Application.Notifications.Localization;
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
        NotificationText detail,
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
    /// Tells an agency owner that a customer messaged one of the agency's members about one of
    /// its listings. RecipientId is the agency's OwnerUserId (this codebase notifies an agency
    /// through its owner). Awareness only: the message goes to the publisher, and its content is
    /// deliberately not part of this notification. RelatedEntityId is the message id. Called by
    /// SendMessageCommandHandler.
    /// </summary>
    Task NotifyAgencyMemberInquiryAsync(
        Guid recipientId,
        Guid messageId,
        Guid propertyId,
        string propertyTitle,
        string agencyName,
        string customerName,
        string memberName,
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
        NotificationText detail,
        CancellationToken ct = default);

    /// <summary>
    /// Service provider onboarding and management: application decisions and provider status
    /// changes. <paramref name="message"/> is rendered in the recipient's language; admin-written
    /// text inside it (a reason, a question) is passed through verbatim.
    /// </summary>
    Task NotifyServiceProviderUpdateAsync(
        Guid recipientId,
        NotificationType type,
        NotificationText message,
        Guid relatedEntityId,
        CancellationToken ct = default);

    /// <summary>
    /// Listing reports: a new report for admins, and an info request or the closing result for the
    /// reporter. <paramref name="dedupeKey"/> makes a retry of the same event a no-op.
    /// </summary>
    Task NotifyListingReportAsync(
        Guid recipientId,
        NotificationType type,
        NotificationText message,
        Guid reportId,
        Guid? propertyId,
        string dedupeKey,
        CancellationToken ct = default);

    /// <summary>
    /// Investment discovery: an admin is told that someone expressed interest in a project.
    /// <paramref name="dedupeKey"/> makes a retry of the same event a no-op.
    /// </summary>
    Task NotifyInvestmentInterestAsync(
        Guid recipientId,
        NotificationText message,
        Guid investmentProjectId,
        string dedupeKey,
        CancellationToken ct = default);

    /// <summary>
    /// QA Phase 13 (P13-D03): tells a user what became of a plan request or listing fee they paid for by hand, or that
    /// their plan is about to end or has ended. <paramref name="type"/> must be one of the plan/fee types;
    /// <paramref name="relatedEntityId"/> is the plan request id or the fee Transaction id.
    /// </summary>
    Task NotifyBillingAsync(
        Guid recipientId,
        NotificationType type,
        NotificationText message,
        Guid? relatedEntityId,
        string? dedupeKey,
        CancellationToken ct = default);

    /// <summary>
    /// A contact-form message arrived (QA Phase 12): sent to admins, RelatedEntityId is the message id.
    /// <paramref name="dedupeKey"/> makes a retry of the same event a no-op.
    /// </summary>
    Task NotifyContactMessageAsync(
        Guid recipientId,
        NotificationText message,
        Guid contactMessageId,
        string dedupeKey,
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

