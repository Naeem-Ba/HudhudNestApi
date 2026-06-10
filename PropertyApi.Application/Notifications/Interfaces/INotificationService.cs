using PropertyApi.Application.Notifications.DTOs;
using PropertyApi.Domain.Notifications.Enums;

namespace PropertyApi.Application.Notifications.Interfaces;

public interface INotificationService
{
    Task NotifyNewMessageAsync(
        Guid recipientId,
        Guid senderId,
        string senderName,
        Guid messageId,
        Guid propertyId,
        CancellationToken ct = default);

    Task NotifyPropertyUpdateAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        NotificationType type,
        string detail,
        CancellationToken ct = default);

    Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);

    Task MarkAllAsReadAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Physically deletes one notification owned by the current user.
    /// Returns false if the notification does not exist or does not belong to the user.
    /// </summary>
    Task<bool> DeleteNotificationAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes one notification owned by the current user by setting IsDeleted/DeletedAt.
    /// Returns false if the notification does not exist or does not belong to the user.
    /// </summary>
    Task<bool> SoftDeleteNotificationAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default);
}
