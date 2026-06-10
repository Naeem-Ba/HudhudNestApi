using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Notifications;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _db;

    public NotificationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Notification>> GetUserNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        return await _db.Notifications
            .AsNoTracking()
            .Where(notification => notification.RecipientId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        return await _db.Notifications
            .AsNoTracking()
            .CountAsync(notification =>
                notification.RecipientId == userId &&
                !notification.IsRead,
                ct);
    }

    public async Task<int> MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.Notifications
            .Where(notification =>
                notification.Id == notificationId &&
                notification.RecipientId == userId &&
                !notification.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(notification => notification.IsRead, true)
                    .SetProperty(notification => notification.ReadAt, now)
                    .SetProperty(notification => notification.UpdatedAt, now),
                ct);
    }

    public async Task<int> MarkAllAsReadAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.Notifications
            .Where(notification =>
                notification.RecipientId == userId &&
                !notification.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(notification => notification.IsRead, true)
                    .SetProperty(notification => notification.ReadAt, now)
                    .SetProperty(notification => notification.UpdatedAt, now),
                ct);
    }

    public async Task<bool> SoftDeleteAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var affectedRows = await _db.Notifications
            .Where(notification =>
                notification.Id == notificationId &&
                notification.RecipientId == userId &&
                !notification.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(notification => notification.IsDeleted, true)
                    .SetProperty(notification => notification.DeletedAt, now)
                    .SetProperty(notification => notification.UpdatedAt, now),
                ct);

        return affectedRows > 0;
    }

    public async Task<bool> DeleteAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var affectedRows = await _db.Notifications
            .IgnoreQueryFilters()
            .Where(notification =>
                notification.Id == notificationId &&
                notification.RecipientId == userId)
            .ExecuteDeleteAsync(ct);

        return affectedRows > 0;
    }
}
