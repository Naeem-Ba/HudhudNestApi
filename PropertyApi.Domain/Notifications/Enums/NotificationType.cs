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
}
