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

