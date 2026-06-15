using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Notifications.DTOs;

public sealed class NotificationDto
{
    public Guid Id { get; init; }
    public NotificationType Type { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? PropertyId { get; init; }
    public Guid? RelatedEntityId { get; init; }
    public bool IsRead { get; init; }
    public DateTime? ReadAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

