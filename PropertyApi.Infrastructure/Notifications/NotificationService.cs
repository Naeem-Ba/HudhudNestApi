using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Notifications.DTOs;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Entities;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Infrastructure.Hubs;
using PropertyApi.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace PropertyApi.Infrastructure.Notifications;

public sealed class NotificationService : INotificationService
{
    private const int MaxPageSize = 50;

    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext db,
        IHubContext<NotificationHub> hub,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyNewMessageAsync(
        Guid recipientId,
        Guid senderId,
        string senderName,
        Guid messageId,
        Guid propertyId,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));

        if (messageId == Guid.Empty)
            throw new ArgumentException("Message id is required.", nameof(messageId));

        if (propertyId == Guid.Empty)
            throw new ArgumentException("Property id is required.", nameof(propertyId));

        var safeSenderName = string.IsNullOrWhiteSpace(senderName)
            ? "مستخدم"
            : senderName.Trim();

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = NotificationType.NewMessage,
            Message = $"أرسل لك {safeSenderName} رسالة جديدة.",
            PropertyId = propertyId,
            RelatedEntityId = messageId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "New message notification created. RecipientId={RecipientId}, SenderId={SenderId}, MessageId={MessageId}, PropertyId={PropertyId}",
            recipientId,
            senderId,
            messageId,
            propertyId);
    }

    public async Task NotifyPropertyUpdateAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        NotificationType type,
        string detail,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));

        if (propertyId == Guid.Empty)
            throw new ArgumentException("Property id is required.", nameof(propertyId));

        var title = string.IsNullOrWhiteSpace(propertyTitle)
            ? "العقار"
            : propertyTitle.Trim();

        var message = type switch
        {
            NotificationType.PropertyStatusChanged =>
                $"تغيّرت حالة عقارك '{title}': {detail}",

            NotificationType.PropertyPriceChanged =>
                $"تغيّر سعر عقارك '{title}': {detail}",

            NotificationType.PropertyPublished =>
                $"تم نشر العقار '{title}' بنجاح.",

            NotificationType.PropertyUnpublished =>
                $"تم إلغاء نشر العقار '{title}'.",

            _ => $"تحديث على العقار '{title}': {detail}"
        };

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = type,
            Message = message,
            PropertyId = propertyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "Property notification created. RecipientId={RecipientId}, PropertyId={PropertyId}, Type={Type}",
            recipientId,
            propertyId,
            type);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return Array.Empty<NotificationDto>();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        return await _db.Notifications
            .AsNoTracking()
            .Where(notification => notification.RecipientId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(notification => new NotificationDto
            {
                Id = notification.Id,
                Type = notification.Type,
                Message = notification.Message,
                PropertyId = notification.PropertyId,
                RelatedEntityId = notification.RelatedEntityId,
                IsRead = notification.IsRead,
                ReadAt = notification.ReadAt,
                CreatedAt = notification.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default)
    {
        if (notificationId == Guid.Empty || userId == Guid.Empty)
            return;

        var now = DateTime.UtcNow;

        await _db.Notifications
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

    public async Task MarkAllAsReadAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return;

        var now = DateTime.UtcNow;

        await _db.Notifications
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

    public async Task<int> GetUnreadCountAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return 0;

        return await _db.Notifications
            .AsNoTracking()
            .CountAsync(notification =>
                notification.RecipientId == userId &&
                !notification.IsRead,
                ct);
    }

    private async Task PersistAndPushAsync(
        Notification notification,
        CancellationToken ct)
    {
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        var dto = MapToDto(notification);
        var groupName = NotificationHub.GetGroupName(notification.RecipientId.ToString());

        await SendSafeAsync(
            groupName,
            "ReceiveNotification",
            dto,
            ct);
    }

    private async Task SendSafeAsync(
        string groupName,
        string method,
        object payload,
        CancellationToken ct)
    {
        try
        {
            await _hub.Clients
                .Group(groupName)
                .SendAsync(method, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SignalR notification push failed. Group={GroupName}, Method={Method}",
                groupName,
                method);
        }
    }

    private static NotificationDto MapToDto(Notification notification)
    {
        return new NotificationDto
        {
            Id = notification.Id,
            Type = notification.Type,
            Message = notification.Message,
            PropertyId = notification.PropertyId,
            RelatedEntityId = notification.RelatedEntityId,
            IsRead = notification.IsRead,
            ReadAt = notification.ReadAt,
            CreatedAt = notification.CreatedAt
        };
    }
}
