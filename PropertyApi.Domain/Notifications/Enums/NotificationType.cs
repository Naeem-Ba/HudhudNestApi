namespace PropertyApi.Domain.Notifications.Enums;

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
    /// AqarTech Services Marketplace: a new ServiceRequest was submitted. Sent to the
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
}
