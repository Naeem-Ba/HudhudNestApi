using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Domain.Notifications.Entities;

/// <summary>
/// Persisted notification for a specific user.
/// It is saved first, then optionally pushed in real-time through SignalR.
/// </summary>
public sealed class Notification : BaseEntity
{
    public Guid RecipientId { get; set; }

    public NotificationType Type { get; set; }

    public string Message { get; set; } = string.Empty;

    public Guid? PropertyId { get; set; }

    public Guid? RelatedEntityId { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public UserAccount? Recipient { get; set; }

    public void MarkAsRead()
    {
        if (IsRead)
            return;

        IsRead = true;
        ReadAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
