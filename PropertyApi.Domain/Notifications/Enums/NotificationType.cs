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
}
