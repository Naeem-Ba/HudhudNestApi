using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Notifications.DTOs;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Entities;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Infrastructure.Hubs;

namespace PropertyApi.Infrastructure.Notifications;

public sealed class NotificationService : INotificationService
{
    private const int MaxPageSize = 50;

    private readonly INotificationRepository _notifications;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRepository notifications,
        IHubContext<NotificationHub> hub,
        ILogger<NotificationService> logger)
    {
        _notifications = notifications;
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
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message id is required.", nameof(messageId));
        }

        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException("Property id is required.", nameof(propertyId));
        }

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
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException("Property id is required.", nameof(propertyId));
        }

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

    public async Task NotifySavedSearchMatchAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        string savedSearchName,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException("Property id is required.", nameof(propertyId));
        }

        var title = string.IsNullOrWhiteSpace(propertyTitle)
            ? "العقار"
            : propertyTitle.Trim();

        var searchName = string.IsNullOrWhiteSpace(savedSearchName)
            ? "بحثك المحفوظ"
            : savedSearchName.Trim();

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = NotificationType.SavedSearchMatch,
            Message = $"عقار جديد '{title}' يطابق '{searchName}'.",
            PropertyId = propertyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "Saved search match notification created. RecipientId={RecipientId}, PropertyId={PropertyId}",
            recipientId,
            propertyId);
    }

    public async Task NotifyUserRatedAsync(
        Guid recipientId,
        Guid raterId,
        string raterName,
        double overallScore,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        var safeRaterName = string.IsNullOrWhiteSpace(raterName)
            ? "مستخدم"
            : raterName.Trim();

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = NotificationType.UserRated,
            Message = $"قيّمك {safeRaterName} بمتوسط {overallScore:0.0}/5 على صفحتك الشخصية.",
            RelatedEntityId = raterId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "User-rated notification created. RecipientId={RecipientId}, RaterId={RaterId}",
            recipientId,
            raterId);
    }

    public async Task NotifyAgencyInvitationReceivedAsync(
        Guid recipientId,
        Guid invitationId,
        string agencyName,
        string inviterName,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Invitation id is required.", nameof(invitationId));
        }

        var agency = string.IsNullOrWhiteSpace(agencyName) ? "مكتب عقاري" : agencyName.Trim();
        var inviter = string.IsNullOrWhiteSpace(inviterName) ? "مستخدم" : inviterName.Trim();

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = NotificationType.AgencyInvitationReceived,
            Message = $"دعاك {inviter} للانضمام إلى مكتب '{agency}'. راجع دعواتك لقبولها أو رفضها.",
            RelatedEntityId = invitationId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "Agency invitation notification created. RecipientId={RecipientId}, InvitationId={InvitationId}",
            recipientId,
            invitationId);
    }

    public async Task NotifyAgencyInvitationRespondedAsync(
        Guid recipientId,
        Guid invitationId,
        string targetUserName,
        bool accepted,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Invitation id is required.", nameof(invitationId));
        }

        var name = string.IsNullOrWhiteSpace(targetUserName) ? "المستخدم" : targetUserName.Trim();

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = accepted
                ? NotificationType.AgencyInvitationAccepted
                : NotificationType.AgencyInvitationDeclined,
            Message = accepted
                ? $"قبل {name} دعوة الانضمام إلى مكتبك."
                : $"رفض {name} دعوة الانضمام إلى مكتبك.",
            RelatedEntityId = invitationId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "Agency invitation response notification created. RecipientId={RecipientId}, InvitationId={InvitationId}, Accepted={Accepted}",
            recipientId,
            invitationId,
            accepted);
    }

    public async Task NotifyShortStayBookingUpdateAsync(
        Guid recipientId,
        Guid bookingId,
        string listingTitle,
        NotificationType type,
        string detail,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        if (bookingId == Guid.Empty)
        {
            throw new ArgumentException("Booking id is required.", nameof(bookingId));
        }

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = type,
            Message = detail,
            // Deliberately RelatedEntityId, not PropertyId — a ShortStayListing is its own
            // aggregate and is not necessarily backed by a Property row.
            RelatedEntityId = bookingId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "Short-stay booking notification created. RecipientId={RecipientId}, BookingId={BookingId}, Type={Type}",
            recipientId,
            bookingId,
            type);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetUserNotificationsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<NotificationDto>();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var notifications = await _notifications.GetUserNotificationsAsync(
            userId,
            page,
            pageSize,
            ct);

        return notifications.Select(MapToDto).ToList();
    }

    public async Task MarkAsReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default)
    {
        if (notificationId == Guid.Empty || userId == Guid.Empty)
        {
            return;
        }

        var affectedRows = await _notifications.MarkAsReadAsync(
            notificationId,
            userId,
            ct);

        if (affectedRows == 0)
        {
            _logger.LogInformation(
                "MarkAsRead ignored. NotificationId={NotificationId}, UserId={UserId}",
                notificationId,
                userId);
        }
    }

    public async Task MarkAllAsReadAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return;
        }

        var affectedRows = await _notifications.MarkAllAsReadAsync(userId, ct);

        _logger.LogInformation(
            "Marked all notifications as read. UserId={UserId}, AffectedRows={AffectedRows}",
            userId,
            affectedRows);
    }

    public async Task<int> GetUnreadCountAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return 0;
        }

        return await _notifications.GetUnreadCountAsync(userId, ct);
    }

    public async Task<bool> DeleteNotificationAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default)
    {
        if (notificationId == Guid.Empty || userId == Guid.Empty)
        {
            return false;
        }

        var deleted = await _notifications.DeleteAsync(notificationId, userId, ct);

        if (deleted)
        {
            _logger.LogInformation(
                "Notification hard-deleted. NotificationId={NotificationId}, UserId={UserId}",
                notificationId,
                userId);
        }
        else
        {
            _logger.LogWarning(
                "Notification hard-delete ignored. NotificationId={NotificationId}, UserId={UserId}",
                notificationId,
                userId);
        }

        return deleted;
    }

    public async Task<bool> SoftDeleteNotificationAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken ct = default)
    {
        if (notificationId == Guid.Empty || userId == Guid.Empty)
        {
            return false;
        }

        var deleted = await _notifications.SoftDeleteAsync(notificationId, userId, ct);

        if (deleted)
        {
            _logger.LogInformation(
                "Notification soft-deleted. NotificationId={NotificationId}, UserId={UserId}",
                notificationId,
                userId);
        }
        else
        {
            _logger.LogWarning(
                "Notification soft-delete ignored. NotificationId={NotificationId}, UserId={UserId}",
                notificationId,
                userId);
        }

        return deleted;
    }

    public async Task NotifyListingExpiringSoonAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        int daysRemaining,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException("Property id is required.", nameof(propertyId));
        }

        var title = string.IsNullOrWhiteSpace(propertyTitle)
            ? "العقار"
            : propertyTitle.Trim();

        // Clamp rather than trust: a clock skew or a sweep that runs late must never
        // produce "ينتهي خلال -2 يوم" in a message a customer reads.
        var days = Math.Max(daysRemaining, 0);

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = NotificationType.ListingExpiringSoon,
            Message = days == 0
                ? $"ينتهي نشر إعلانك '{title}' اليوم. مدِّده للإبقاء عليه ظاهراً."
                : $"ينتهي نشر إعلانك '{title}' خلال {days} يوماً. مدِّده للإبقاء عليه ظاهراً.",
            PropertyId = propertyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "Listing expiring-soon notification created. RecipientId={RecipientId}, PropertyId={PropertyId}, DaysRemaining={Days}",
            recipientId,
            propertyId,
            days);
    }

    public async Task NotifyListingExpiredAsync(
        Guid recipientId,
        Guid propertyId,
        string propertyTitle,
        int graceDaysRemaining,
        CancellationToken ct = default)
    {
        if (recipientId == Guid.Empty)
        {
            throw new ArgumentException("Recipient id is required.", nameof(recipientId));
        }

        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException("Property id is required.", nameof(propertyId));
        }

        var title = string.IsNullOrWhiteSpace(propertyTitle)
            ? "العقار"
            : propertyTitle.Trim();

        var graceDays = Math.Max(graceDaysRemaining, 0);

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = NotificationType.ListingExpired,
            Message =
                $"انتهت مدة نشر إعلانك '{title}' ولم يعد ظاهراً في نتائج البحث. " +
                $"أمامك {graceDays} يوماً لتمديده قبل حذفه.",
            PropertyId = propertyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "Listing expired notification created. RecipientId={RecipientId}, PropertyId={PropertyId}, GraceDaysRemaining={GraceDays}",
            recipientId,
            propertyId,
            graceDays);
    }

    private async Task PersistAndPushAsync(
        Notification notification,
        CancellationToken ct)
    {
        await _notifications.AddAsync(notification, ct);

        var dto = MapToDto(notification);
        var groupName = NotificationHub.GetGroupName(notification.RecipientId);

        await SendSafeAsync(
            groupName,
            NotificationHub.ReceiveNotificationEvent,
            dto,
            ct);
    }

    private async Task SendSafeAsync(
        string groupName,
        string method,
        NotificationDto payload,
        CancellationToken ct)
    {
        try
        {
            await _hub.Clients
                .Group(groupName)
                .SendAsync(method, payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SignalR notification push failed. Group={GroupName}, Method={Method}, NotificationId={NotificationId}",
                groupName,
                method,
                payload.Id);
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
