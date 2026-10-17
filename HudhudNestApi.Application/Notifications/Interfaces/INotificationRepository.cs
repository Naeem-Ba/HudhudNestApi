using HudhudNestApi.Domain.Notifications.Entities;

namespace HudhudNestApi.Application.Notifications.Interfaces;

public interface INotificationRepository
{
    /// <summary>
    /// Stores the notification. Returns false (and stores nothing) when one with the same
    /// <c>DedupeKey</c> already exists for the recipient, so callers skip the live and push delivery.
    /// </summary>
    Task<bool> AddAsync(Notification notification, CancellationToken ct = default);

    Task<IReadOnlyList<Notification>> GetUserNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
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

    /// <summary>
    /// Retention: permanently removes (at most <paramref name="batchSize"/>) notifications that were read
    /// before <paramref name="readBefore"/> or soft-deleted before <paramref name="softDeletedBefore"/>.
    /// Returns the removed ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> DeleteExpiredAsync(
        DateTime readBefore,
        DateTime softDeletedBefore,
        int batchSize,
        CancellationToken ct = default);
}
