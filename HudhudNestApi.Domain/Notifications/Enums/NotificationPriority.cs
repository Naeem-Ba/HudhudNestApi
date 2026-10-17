namespace HudhudNestApi.Domain.Notifications.Enums;

/// <summary>
/// How urgent a notification is. Stored as an integer, so values are never renumbered.
/// High: a transaction or account event the recipient must act on. Normal: messages, requests and
/// status changes. Low: optional updates (ratings, price changes).
/// </summary>
public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}
