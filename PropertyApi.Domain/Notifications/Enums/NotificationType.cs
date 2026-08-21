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
}
