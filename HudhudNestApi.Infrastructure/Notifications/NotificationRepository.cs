using Microsoft.EntityFrameworkCore;
using Npgsql;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Paging;

namespace HudhudNestApi.Infrastructure.Notifications;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _db;

    public NotificationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> AddAsync(Notification notification, CancellationToken ct = default)
    {
        _db.Notifications.Add(notification);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex)
            when (notification.DedupeKey is not null
                  && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same business event was already notified (a retry, or two instances racing): not an error.
            _db.Entry(notification).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<Guid>> DeleteExpiredAsync(
        DateTime readBefore,
        DateTime softDeletedBefore,
        int batchSize,
        CancellationToken ct = default)
    {
        // Read notifications past retention, and ones the user removed past the undo window.
        var ids = await _db.Notifications
            .IgnoreQueryFilters()
            .Where(n => (n.IsRead && n.ReadAt != null && n.ReadAt < readBefore)
                        || (n.IsDeleted && n.DeletedAt != null && n.DeletedAt < softDeletedBefore))
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (ids.Count > 0)
        {
            await _db.Notifications.IgnoreQueryFilters().Where(n => ids.Contains(n.Id)).ExecuteDeleteAsync(ct);
        }

        return ids;
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
            .SkipPage(page, pageSize)
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
