namespace HudhudNestApi.Application.Notifications.Localization;

/// <summary>Catalog keys for the sentences handlers pass to <see cref="Interfaces.INotificationService"/>.</summary>
public static class NotificationKeys
{
    // Visit details (the part after the property title).
    public const string VisitRequestedDetail = "visit.detail.requested";
    public const string VisitConfirmedDetail = "visit.detail.confirmed";
    public const string VisitDeclinedDetail = "visit.detail.declined";
    public const string VisitCancelledDetail = "visit.detail.cancelled";
    public const string VisitCancelledListingRemovedDetail = "visit.detail.cancelled_listing_removed";
    public const string VisitProposedDetail = "visit.detail.proposed";
    public const string VisitRescheduleAcceptedDetail = "visit.detail.reschedule_accepted";
    public const string VisitRescheduleDeclinedDetail = "visit.detail.reschedule_declined";
    public const string NoteFromOwner = "note.owner";
    public const string NoteDeclineReason = "note.decline_reason";
    public const string NoteFromVisitor = "note.visitor";

    // Property/listing details.
    public const string StatusChangedDetail = "detail.status_change";
    public const string ColdRentChangedDetail = "detail.cold_rent";
    public const string WarmRentChangedDetail = "detail.warm_rent";
    public const string PurchasePriceChangedDetail = "detail.purchase_price";
    public const string ReviewAddedDetail = "detail.review_added";

    // Service marketplace details.
    public const string ServiceSubmittedDetail = "service.detail.submitted";
    public const string ServiceAcceptedDetail = "service.detail.accepted";
    public const string ServiceRejectedDetail = "service.detail.rejected";
    public const string ServiceScheduledDetail = "service.detail.scheduled";
    public const string ServiceCompletedDetail = "service.detail.completed";
    public const string ServiceCancelledDetail = "service.detail.cancelled";
    public const string ServiceReviewAddedDetail = "service.detail.review_added";
    public const string ServiceNotCompletedDetail = "service.detail.not_completed";
    public const string ServiceInquiryDetail = "service.detail.inquiry";
    public const string ServiceMessageDetail = "service.detail.message";
    public const string ServiceConfirmReminderDetail = "service.detail.confirm_reminder";
    public const string ServiceExpiredDetail = "service.detail.expired";

    // Service provider onboarding and management.
    public const string ProviderApplicationInfoRequested = "msg.provider_application_info_requested";
    public const string ProviderApplicationApproved = "msg.provider_application_approved";
    public const string ProviderApplicationRejected = "msg.provider_application_rejected";
    public const string ProviderSuspended = "msg.provider_suspended";
    public const string ProviderReactivated = "msg.provider_reactivated";
    public const string ProviderRevoked = "msg.provider_revoked";

    // Short-stay details.
    public const string ShortStayRequestedDetail = "shortstay.detail.requested";
    public const string ShortStayApprovedDetail = "shortstay.detail.approved";
    public const string ShortStayOtherBookingTakenDetail = "shortstay.detail.other_booking_taken";
    public const string ShortStayConfirmedDetail = "shortstay.detail.confirmed";
    public const string ShortStayRejectedDetail = "shortstay.detail.rejected";
    public const string ShortStayCancelledByGuestDetail = "shortstay.detail.cancelled_by_guest";
    public const string ShortStayReviewAddedDetail = "shortstay.detail.review_added";
    public const string ShortStayExpiredGuestDetail = "shortstay.detail.expired_guest";
    public const string ShortStayExpiredHostDetail = "shortstay.detail.expired_host";
    public const string ShortStayListingRestoredDetail = "shortstay.detail.listing_restored";
}
