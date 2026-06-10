namespace PropertyApi.Domain.Notifications.Enums;

/// <summary>
/// Supported notification events in the platform.
/// Keep numeric values stable because they are persisted in the database.
/// </summary>
public enum NotificationType
{
    NewMessage = 1,
    PropertyStatusChanged = 2,
    PropertyPriceChanged = 3,
    PropertyPublished = 4,
    PropertyUnpublished = 5
}
