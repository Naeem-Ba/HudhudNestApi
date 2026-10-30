namespace HudhudNestApi.Domain.Notifications.Enums;

public enum NotificationType
{
    NewMessage = 1,
    PropertyStatusChanged = 2,
    PropertyPriceChanged = 3,
    PropertyPublished = 4,
    PropertyUnpublished = 5,

    VisitRequested = 6,
    VisitConfirmed = 7,
    VisitDeclined = 8,
    VisitCancelled = 9,

    ReviewAdded = 10,
    PhoneVerification = 11,

    /// <summary>Phase-0, Task 3 — a newly published property matched one of the user's saved searches.</summary>
    SavedSearchMatch = 12,

    /// <summary>Another user submitted a UserRating (public profile trust score) about this user.</summary>
    UserRated = 13,

    /// <summary>
    /// The owner's listing is approaching the end of its publication window.
    /// Raised once per publication period by ListingExpiryHostedService — the
    /// Property.ExpiryWarningSentAt stamp is what keeps it from repeating every sweep.
    /// </summary>
    ListingExpiringSoon = 14,

    /// <summary>
    /// The owner's listing has expired: hidden from search, and on a grace clock
    /// (ListingLifecyclePolicy.GracePeriodBeforeDeletion) after which it is deleted.
    /// This is the notification the user acts on to pay for an extension.
    /// </summary>
    ListingExpired = 15,

    /// <summary>
    /// B-2 (RELEASE-BLOCKERS-AR.md): an agency owner sent the recipient an AgencyInvitation.
    /// Raised by CreateAgencyInvitationCommandHandler; RelatedEntityId is the invitation id.
    /// </summary>
    AgencyInvitationReceived = 16,

    /// <summary>The recipient's agency invitation was accepted. Sent to the agency owner.</summary>
    AgencyInvitationAccepted = 17,

    /// <summary>The recipient's agency invitation was declined. Sent to the agency owner.</summary>
    AgencyInvitationDeclined = 18,

    /// <summary>
    /// HudhudNest Services Marketplace: a new ServiceRequest was submitted. Sent to the
    /// ServiceProvider's UserId. Raised by CreateServiceRequestCommandHandler.
    /// </summary>
    ServiceRequestSubmitted = 19,

    /// <summary>The provider accepted a ServiceRequest. Sent to the requester.</summary>
    ServiceRequestAccepted = 20,

    /// <summary>The provider rejected a ServiceRequest. Sent to the requester.</summary>
    ServiceRequestRejected = 21,

    /// <summary>The provider scheduled a ServiceRequest. Sent to the requester.</summary>
    ServiceRequestScheduled = 22,

    /// <summary>The provider marked a ServiceRequest completed. Sent to the requester.</summary>
    ServiceRequestCompleted = 23,

    /// <summary>Either side cancelled a ServiceRequest. Sent to the other side.</summary>
    ServiceRequestCancelled = 24,

    /// <summary>A ServiceReview was added. Sent to the ServiceProvider's UserId.</summary>
    ServiceReviewAdded = 25,

    /// <summary>Short-stay: a guest submitted a new booking (Request or Instant). Sent to the
    /// listing owner. Raised by CreateBookingCommandHandler.</summary>
    ShortStayBookingRequested = 26,

    /// <summary>Short-stay: the host approved a Pending booking. Sent to the guest.</summary>
    ShortStayBookingApproved = 27,

    /// <summary>Short-stay: the host rejected a booking, or it lost out to another approved
    /// overlapping request. Sent to the guest.</summary>
    ShortStayBookingRejected = 28,

    /// <summary>Short-stay: a booking reached BookingStatus.Confirmed. Sent to the guest.</summary>
    ShortStayBookingConfirmed = 29,

    /// <summary>Short-stay: a booking was cancelled by the guest. Sent to the host.</summary>
    ShortStayBookingCancelled = 30,

    /// <summary>Short-stay: a new review was left for a listing. Sent to the host.</summary>
    ShortStayReviewAdded = 31,

    /// <summary>The owner proposed an alternate date/time for a Pending VisitRequest. Sent to the requester.</summary>
    VisitRescheduleProposed = 32,

    /// <summary>The requester accepted the owner's alternate date/time. Sent to the property owner.</summary>
    VisitRescheduleAccepted = 33,

    /// <summary>The requester declined the owner's alternate date/time. Sent to the property owner.</summary>
    VisitRescheduleDeclined = 34,

    /// <summary>
    /// Valuation Stage 5 (24h SLA): a ValuationInquiry passed its 24h ExpiresAt without
    /// reaching Completed. Sent to the requester (RequesterId) — never raised for an
    /// anonymous inquiry (RequesterId null), since there is no account to notify. Raised by
    /// ValuationInquiryExpiryHostedService via ValuationSlaEnforcementService.
    /// </summary>
    ValuationInquiryExpired = 35,

    /// <summary>
    /// Valuation Stage 5 (24h SLA): a ValuationOfficeInvitation was still Sent (unanswered)
    /// when its parent inquiry's SLA window closed. Sent to the invited agency's owner
    /// (Agency.OwnerUserId), same resolution AgencyInvitation notifications already use.
    /// Raised by ValuationInquiryExpiryHostedService via ValuationSlaEnforcementService.
    /// </summary>
    ValuationOfficeInvitationExpired = 36,

    /// <summary>
    /// Remediation H2 — a ValuationInquiry reached Completed because every invitation
    /// OfficeMatchingService sent for it has now been answered (no more Sent invitations
    /// remain), so the customer no longer has to wait out the full 24h SLA window to see a
    /// result. Sent to the requester (RequesterId) — never raised for an anonymous inquiry,
    /// same rule ValuationInquiryExpired already applies. Raised by
    /// SubmitOfficeResponseCommandHandler when the last outstanding response arrives, and
    /// retried by ValuationSlaEnforcementService (ResultReadyNotifiedAt) if that first attempt
    /// fails.
    /// </summary>
    ValuationResultReady = 37,

    /// <summary>
    /// Remediation M4 — a ValuationInquiry has been open for 18h without reaching a terminal
    /// state (Completed/Expired), warning the requester the 24h SLA window is closing soon.
    /// Sent at most once per inquiry (ValuationInquiry.ReminderSentAt is the idempotency
    /// stamp) — never raised for an anonymous inquiry, same rule as the other Valuation
    /// notifications. Raised by ValuationInquiryExpiryHostedService via
    /// ValuationSlaEnforcementService.
    /// </summary>
    ValuationInquiryReminderSoon = 38,

    /// <summary>
    /// A customer messaged an agency member about one of the agency's listings. Sent to the
    /// agency owner as an awareness notice only: the message itself still goes to the listing
    /// publisher, and its content is not copied here. RelatedEntityId is the message id.
    /// Raised by SendMessageCommandHandler.
    /// </summary>
    AgencyMemberInquiry = 39,

    // ── Service provider onboarding and management. RelatedEntityId is the
    // ServiceProviderApplication id for the application events and the ServiceProvider id
    // for the provider status events. Raised by the provider application/admin handlers.

    /// <summary>An admin asked the applicant for more information.</summary>
    ServiceProviderApplicationInfoRequested = 40,

    /// <summary>The application was approved and the provider profile is active.</summary>
    ServiceProviderApplicationApproved = 41,

    /// <summary>The application was rejected, with the reason.</summary>
    ServiceProviderApplicationRejected = 42,

    /// <summary>The provider was suspended, reactivated or had its approval revoked.</summary>
    ServiceProviderStatusChanged = 43,

    /// <summary>
    /// An administrator took the recipient's listing down. The message carries the reason; the listing is
    /// hidden and cannot be re-published or extended by its owner.
    /// </summary>
    ListingTakenDown = 44,

    /// <summary>An administrator lifted a take-down and the listing is public again.</summary>
    ListingRestored = 45,

    /// <summary>
    /// A visit the recipient had requested was cancelled because the listing was deleted or removed.
    /// Sent to the visitor; RelatedEntityId is the visit request id.
    /// </summary>
    VisitCancelledByHost = 46,

    /// <summary>The provider closed an accepted ServiceRequest as not delivered. Sent to the requester,
    /// who is asked why it did not happen from their side.</summary>
    ServiceRequestNotCompleted = 47,

    /// <summary>A customer opened a conversation about a service. Sent to the provider.</summary>
    ServiceInquiryReceived = 48,

    /// <summary>A new message in a service request thread. Sent to the other side.</summary>
    ServiceRequestMessage = 49,

    /// <summary>The customer has not confirmed an inquiry after two days. Sent to the customer.</summary>
    ServiceRequestConfirmReminder = 50,

    /// <summary>A month without contact: the request was closed by the system. Sent to both sides.</summary>
    ServiceRequestExpired = 51,

    // ── Listing reports (moderation). RelatedEntityId is the ListingReport id.

    /// <summary>A listing was reported. Sent to admins (never names the reporter).</summary>
    ListingReportSubmitted = 52,

    /// <summary>An admin asked the reporter for more information about their report.</summary>
    ListingReportInfoRequested = 53,

    /// <summary>The reporter's report was closed; the message says only whether action was taken.</summary>
    ListingReportClosed = 54,

    // ── Investment discovery. RelatedEntityId is the InvestmentProject id.

    /// <summary>Someone expressed interest in an investment project. Sent to admins (never names the person).</summary>
    InvestmentInterestReceived = 55,

    // ── Contact form. RelatedEntityId is the ContactMessage id.

    /// <summary>A visitor sent a message through the public contact form. Sent to admins.</summary>
    ContactMessageReceived = 56,
}
