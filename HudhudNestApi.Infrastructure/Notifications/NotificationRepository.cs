using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Models;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Notifications;

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

    public async Task<IReadOnlyDictionary<Guid, NotificationMessageContext>> GetMessageContextsAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return new Dictionary<Guid, NotificationMessageContext>();

        var rows = await (
                from m in _db.Messages.AsNoTracking()
                where messageIds.Contains(m.Id)
                join s in _db.UserAccounts on m.SenderId equals s.Id into senders
                from s in senders.DefaultIfEmpty()
                join r in _db.UserAccounts on m.ReceiverId equals r.Id into receivers
                from r in receivers.DefaultIfEmpty()
                join p in _db.Properties on m.PropertyId equals p.Id into properties
                from p in properties.DefaultIfEmpty()
                join a in _db.Agencies on p.AgencyId equals a.Id into agencies
                from a in agencies.DefaultIfEmpty()
                select new
                {
                    m.Id,
                    SenderDisplay = s.DisplayName,
                    SenderFirst = s.FirstName,
                    SenderLast = s.LastName,
                    ReceiverDisplay = r.DisplayName,
                    ReceiverFirst = r.FirstName,
                    ReceiverLast = r.LastName,
                    PropertyTitle = p.Title,
                    AgencyName = a.Name
                })
            .ToListAsync(ct);

        return rows.ToDictionary(
            row => row.Id,
            row => new NotificationMessageContext(
                SenderName: PersonName(row.SenderDisplay, row.SenderFirst, row.SenderLast),
                ReceiverName: PersonName(row.ReceiverDisplay, row.ReceiverFirst, row.ReceiverLast),
                PropertyTitle: row.PropertyTitle,
                AgencyName: row.AgencyName));
    }

    private static string? PersonName(string? display, string? first, string? last)
    {
        if (!string.IsNullOrWhiteSpace(display))
            return display;

        var full = $"{first} {last}".Trim();
        return full.Length == 0 ? null : full;
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
