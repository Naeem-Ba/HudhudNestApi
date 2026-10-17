using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Notifications.DTOs;

public sealed class NotificationDto
{
    public Guid Id { get; init; }
    public NotificationType Type { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? PropertyId { get; init; }
    public Guid? RelatedEntityId { get; init; }
    /// <summary>Filter group, e.g. "messages" or "visits".</summary>
    public string Category { get; init; } = string.Empty;
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
    /// <summary>Which screen the notification opens; the screen re-checks access.</summary>
    public NotificationTarget? Target { get; init; }
    public bool IsRead { get; init; }
    public DateTime? ReadAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

