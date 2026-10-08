using HudhudNestApi.Application.Notifications.Models;
using HudhudNestApi.Domain.Notifications.Entities;

namespace HudhudNestApi.Application.Notifications.Interfaces;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);

    Task<IReadOnlyList<Notification>> GetUserNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Names and titles behind message-based notifications, for rendering their text in the
    /// reader's language. Keyed by message id; a message that no longer resolves is absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, NotificationMessageContext>> GetMessageContextsAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);

    Task<int> MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);

    Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);
}

