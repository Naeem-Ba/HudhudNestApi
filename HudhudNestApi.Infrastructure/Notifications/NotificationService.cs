using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications;
using HudhudNestApi.Application.Notifications.DTOs;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Preferences;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Notifications.Push;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Hubs;
using HudhudNestApi.Infrastructure.Notifications.Localization;

namespace HudhudNestApi.Infrastructure.Notifications;

/// <summary>
/// Creates, stores and delivers notifications. Every sentence is rendered here, in the language
/// of the RECIPIENT (see <see cref="IRecipientLanguageResolver"/>), from the central
/// <see cref="NotificationCatalog"/> -- callers describe what happened, never how it is worded.
/// Notifications already stored are never re-rendered.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private const int MaxPageSize = 50;
    private const string PushTitle = "HudhudNest";
    private static readonly TimeSpan PushBudget = TimeSpan.FromSeconds(5);

    private readonly INotificationRepository _notifications;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<NotificationService> _logger;
    private readonly IRecipientLanguageResolver? _languages;
    private readonly IPushNotificationDispatcher? _push;
    private readonly INotificationPreferenceStore? _preferences;

    public NotificationService(
        INotificationRepository notifications,
        IHubContext<NotificationHub> hub,
        ILogger<NotificationService> logger,
        IRecipientLanguageResolver? languages = null,
        IPushNotificationDispatcher? push = null,
        INotificationPreferenceStore? preferences = null)
    {
        _notifications = notifications;
        _hub = hub;
        _logger = logger;
        _languages = languages;
        _push = push;
        _preferences = preferences;
    }

    public async Task NotifyNewMessageAsync(
        Guid recipientId,
        Guid senderId,
        string senderName,
        Guid messageId,
        Guid propertyId,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(messageId, "Message id is required.", nameof(messageId));
        RequireId(propertyId, "Property id is required.", nameof(propertyId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.NewMessage,
                Say(lang, "msg.new_message", ("sender", NameOr(senderName, "generic.user"))),
                propertyId: propertyId, relatedEntityId: messageId,
                dedupeKey: $"msg:{messageId:N}"),
            ct);

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
        NotificationText detail,
        Guid? relatedEntityId = null,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(propertyId, "Property id is required.", nameof(propertyId));

        var lang = await LanguageOfAsync(recipientId, ct);

        var key = type switch
        {
            NotificationType.PropertyStatusChanged => "msg.property_status_changed",
            NotificationType.PropertyPriceChanged => "msg.property_price_changed",
            NotificationType.PropertyPublished => "msg.property_published",
            NotificationType.PropertyUnpublished => "msg.property_unpublished",
            NotificationType.VisitRequested => "msg.visit_requested",
            NotificationType.VisitConfirmed => "msg.visit_confirmed",
            NotificationType.VisitDeclined => "msg.visit_declined",
            NotificationType.VisitCancelled => "msg.visit_cancelled",
            NotificationType.VisitRescheduleProposed => "msg.visit_reschedule_proposed",
            NotificationType.VisitRescheduleAccepted => "msg.visit_reschedule_accepted",
            NotificationType.VisitRescheduleDeclined => "msg.visit_reschedule_declined",
            NotificationType.VisitCancelledByHost => "msg.visit_cancelled_by_host",
            NotificationType.ListingTakenDown => "msg.listing_taken_down",
            NotificationType.ListingRestored => "msg.listing_restored",
            _ => "msg.property_update_default",
        };

        var message = Say(
            lang,
            key,
            ("title", NameOr(propertyTitle, "generic.property")),
            ("detail", detail ?? NotificationText.Empty));

        await PersistAndPushAsync(
            New(recipientId, type, message, propertyId: propertyId, relatedEntityId: relatedEntityId),
            ct);

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
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(propertyId, "Property id is required.", nameof(propertyId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.SavedSearchMatch,
                Say(lang, "msg.saved_search_match",
                    ("title", NameOr(propertyTitle, "generic.property")),
                    ("search", NameOr(savedSearchName, "generic.saved_search"))),
                propertyId: propertyId,
                dedupeKey: $"ssm:{propertyId:N}"),
            ct);

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
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.UserRated,
                Say(lang, "msg.user_rated",
                    ("rater", NameOr(raterName, "generic.user")),
                    ("score", overallScore)),
                relatedEntityId: raterId),
            ct);

        _logger.LogInformation(
            "User-rated notification created. RecipientId={RecipientId}, RaterId={RaterId}",
            recipientId,
            raterId);
    }

    public async Task NotifyAgencyMemberInquiryAsync(
        Guid recipientId,
        Guid messageId,
        Guid propertyId,
        string propertyTitle,
        string agencyName,
        string customerName,
        string memberName,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(messageId, "Message id is required.", nameof(messageId));
        RequireId(propertyId, "Property id is required.", nameof(propertyId));

        var lang = await LanguageOfAsync(recipientId, ct);

        // Awareness only: names the people, the listing and the agency, never the message text.
        var notification = New(
            recipientId,
            NotificationType.AgencyMemberInquiry,
            Say(lang, "msg.agency_member_inquiry",
                ("customer", NameOr(customerName, "generic.user")),
                ("member", NameOr(memberName, "generic.agency_member")),
                ("title", NameOr(propertyTitle, "generic.property_noun")),
                ("agency", NameOr(agencyName, "generic.agency"))),
            propertyId: propertyId,
            relatedEntityId: messageId,
            dedupeKey: $"ami:{messageId:N}");

        await PersistAndPushAsync(notification, ct);

        _logger.LogInformation(
            "Agency member inquiry notification created. NotificationId={NotificationId}, RecipientId={RecipientId}, PropertyId={PropertyId}, MessageId={MessageId}",
            notification.Id,
            recipientId,
            propertyId,
            messageId);
    }

    public async Task NotifyServiceProviderUpdateAsync(
        Guid recipientId,
        NotificationType type,
        NotificationText message,
        Guid relatedEntityId,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(relatedEntityId, "Related entity id is required.", nameof(relatedEntityId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, type,
                NotificationCatalog.Render(message ?? NotificationText.Empty, lang),
                relatedEntityId: relatedEntityId),
            ct);

        _logger.LogInformation(
            "Service provider notification created. RecipientId={RecipientId}, Type={Type}, RelatedEntityId={RelatedEntityId}",
            recipientId,
            type,
            relatedEntityId);
    }

    public async Task NotifyAgencyInvitationReceivedAsync(
        Guid recipientId,
        Guid invitationId,
        string agencyName,
        string inviterName,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(invitationId, "Invitation id is required.", nameof(invitationId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.AgencyInvitationReceived,
                Say(lang, "msg.agency_invitation_received",
                    ("inviter", NameOr(inviterName, "generic.user")),
                    ("agency", NameOr(agencyName, "generic.agency"))),
                relatedEntityId: invitationId,
                dedupeKey: $"agi:{invitationId:N}"),
            ct);

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
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(invitationId, "Invitation id is required.", nameof(invitationId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId,
                accepted
                    ? NotificationType.AgencyInvitationAccepted
                    : NotificationType.AgencyInvitationDeclined,
                Say(lang,
                    accepted ? "msg.agency_invitation_accepted" : "msg.agency_invitation_declined",
                    ("name", NameOr(targetUserName, "generic.the_user"))),
                relatedEntityId: invitationId),
            ct);

        _logger.LogInformation(
            "Agency invitation response notification created. RecipientId={RecipientId}, InvitationId={InvitationId}, Accepted={Accepted}",
            recipientId,
            invitationId,
            accepted);
    }

    public async Task NotifyValuationInquiryExpiredAsync(
        Guid recipientId,
        Guid inquiryId,
        bool hadPreliminaryEstimate,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(inquiryId, "Inquiry id is required.", nameof(inquiryId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.ValuationInquiryExpired,
                Say(lang, hadPreliminaryEstimate
                    ? "msg.valuation_expired_with_estimate"
                    : "msg.valuation_expired_no_estimate"),
                relatedEntityId: inquiryId,
                dedupeKey: $"val-exp:{inquiryId:N}"),
            ct);

        _logger.LogInformation(
            "Valuation inquiry expiry notification created. RecipientId={RecipientId}, InquiryId={InquiryId}, HadPreliminaryEstimate={HadPreliminaryEstimate}",
            recipientId,
            inquiryId,
            hadPreliminaryEstimate);
    }

    public async Task NotifyValuationOfficeInvitationExpiredAsync(
        Guid recipientId,
        Guid invitationId,
        Guid inquiryId,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(invitationId, "Invitation id is required.", nameof(invitationId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.ValuationOfficeInvitationExpired,
                Say(lang, "msg.valuation_office_invitation_expired"),
                relatedEntityId: invitationId,
                dedupeKey: $"val-inv-exp:{invitationId:N}"),
            ct);

        _logger.LogInformation(
            "Valuation office invitation expiry notification created. RecipientId={RecipientId}, InvitationId={InvitationId}, InquiryId={InquiryId}",
            recipientId,
            invitationId,
            inquiryId);
    }

    public async Task NotifyValuationResultReadyAsync(
        Guid recipientId,
        Guid inquiryId,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(inquiryId, "Inquiry id is required.", nameof(inquiryId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.ValuationResultReady,
                Say(lang, "msg.valuation_result_ready"),
                relatedEntityId: inquiryId,
                dedupeKey: $"val-ready:{inquiryId:N}"),
            ct);

        _logger.LogInformation(
            "Valuation result-ready notification created. RecipientId={RecipientId}, InquiryId={InquiryId}",
            recipientId,
            inquiryId);
    }

    public async Task NotifyValuationInquiryReminderSoonAsync(
        Guid recipientId,
        Guid inquiryId,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(inquiryId, "Inquiry id is required.", nameof(inquiryId));

        var lang = await LanguageOfAsync(recipientId, ct);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.ValuationInquiryReminderSoon,
                Say(lang, "msg.valuation_reminder_soon"),
                relatedEntityId: inquiryId,
                dedupeKey: $"val-rem:{inquiryId:N}"),
            ct);

        _logger.LogInformation(
            "Valuation inquiry reminder-soon notification created. RecipientId={RecipientId}, InquiryId={InquiryId}",
            recipientId,
            inquiryId);
    }

    public async Task NotifyShortStayBookingUpdateAsync(
        Guid recipientId,
        Guid bookingId,
        string listingTitle,
        NotificationType type,
        NotificationText detail,
        CancellationToken ct = default)
    {
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(bookingId, "Booking id is required.", nameof(bookingId));

        var lang = await LanguageOfAsync(recipientId, ct);

        // Deliberately RelatedEntityId, not PropertyId -- a ShortStayListing is its own
        // aggregate and is not necessarily backed by a Property row.
        await PersistAndPushAsync(
            New(recipientId, type,
                NotificationCatalog.Render(detail ?? NotificationText.Empty, lang),
                relatedEntityId: bookingId),
            ct);

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
            return;
        }

        await BroadcastReadStateAsync(userId, new[] { notificationId }, ct);
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

        if (affectedRows > 0)
        {
            await BroadcastReadStateAsync(userId, null, ct);
        }
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
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(propertyId, "Property id is required.", nameof(propertyId));

        var lang = await LanguageOfAsync(recipientId, ct);

        // Clamp rather than trust: a clock skew or a sweep that runs late must never
        // produce "expires in -2 days" in a message a customer reads.
        var days = Math.Max(daysRemaining, 0);

        var title = NameOr(propertyTitle, "generic.property");
        var message = days == 0
            ? Say(lang, "msg.listing_expiring_today", ("title", title))
            : Say(lang, "msg.listing_expiring_days", ("title", title), ("days", days));

        await PersistAndPushAsync(
            New(recipientId, NotificationType.ListingExpiringSoon, message, propertyId: propertyId),
            ct);

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
        RequireId(recipientId, "Recipient id is required.", nameof(recipientId));
        RequireId(propertyId, "Property id is required.", nameof(propertyId));

        var lang = await LanguageOfAsync(recipientId, ct);
        var graceDays = Math.Max(graceDaysRemaining, 0);

        await PersistAndPushAsync(
            New(recipientId, NotificationType.ListingExpired,
                Say(lang, "msg.listing_expired",
                    ("title", NameOr(propertyTitle, "generic.property")),
                    ("days", graceDays)),
                propertyId: propertyId),
            ct);

        _logger.LogInformation(
            "Listing expired notification created. RecipientId={RecipientId}, PropertyId={PropertyId}, GraceDaysRemaining={GraceDays}",
            recipientId,
            propertyId,
            graceDays);
    }

    // ------------------------------------------------------------------ helpers

    private static void RequireId(Guid id, string message, string paramName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(message, paramName);
        }
    }

    private async Task<string> LanguageOfAsync(Guid recipientId, CancellationToken ct)
    {
        if (_languages is null)
        {
            return SupportedPreferences.FallbackLanguage;
        }

        try
        {
            return await _languages.GetLanguageAsync(recipientId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A lookup failure must not lose the notification: fall back to the product language.
            _logger.LogWarning(ex, "Recipient language lookup failed. RecipientId={RecipientId}", recipientId);
            return SupportedPreferences.FallbackLanguage;
        }
    }

    /// <summary>A user-supplied name is kept verbatim (never translated); a blank one becomes a translated placeholder.</summary>
    private static NotificationText NameOr(string? value, string fallbackKey) =>
        string.IsNullOrWhiteSpace(value)
            ? NotificationText.Of(fallbackKey)
            : NotificationText.FromLiteral(value.Trim());

    private static string Say(string lang, string key, params (string Name, object? Value)[] args) =>
        NotificationCatalog.Render(NotificationText.Of(key, args), lang);

    private static Notification New(
        Guid recipientId,
        NotificationType type,
        string message,
        Guid? propertyId = null,
        Guid? relatedEntityId = null,
        string? dedupeKey = null) =>
        new()
        {
            RecipientId = recipientId,
            Type = type,
            Message = message,
            Priority = NotificationKindCatalog.Describe(type).Priority,
            DedupeKey = dedupeKey,
            PropertyId = propertyId,
            RelatedEntityId = relatedEntityId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    private async Task PersistAndPushAsync(
        Notification notification,
        CancellationToken ct)
    {
        var kind = NotificationKindCatalog.Describe(notification.Type);
        var prefs = await PreferencesOfAsync(notification.RecipientId, ct);

        // The recipient switched this category off (mandatory events are never switched off).
        if (!NotificationDeliveryPolicy.ShouldStore(prefs, kind))
        {
            _logger.LogInformation(
                "Notification skipped by the recipient's preferences. RecipientId={RecipientId}, Type={Type}",
                notification.RecipientId,
                notification.Type);
            return;
        }

        if (!await _notifications.AddAsync(notification, ct))
        {
            // Same business event already notified: no second live or device delivery.
            _logger.LogInformation(
                "Duplicate notification ignored. RecipientId={RecipientId}, Type={Type}",
                notification.RecipientId,
                notification.Type);
            return;
        }

        var dto = MapToDto(notification);
        var groupName = NotificationHub.GetGroupName(notification.RecipientId);

        await SendSafeAsync(
            groupName,
            NotificationHub.ReceiveNotificationEvent,
            dto,
            ct);

        var decision = NotificationDeliveryPolicy.DecidePush(prefs, kind, DateTime.UtcNow);
        if (decision == PushDecision.Skip)
        {
            return;
        }

        await PushSafeAsync(notification, decision == PushDecision.Generic, kind, ct);
    }

    private async Task<NotificationPreference?> PreferencesOfAsync(Guid recipientId, CancellationToken ct)
    {
        if (_preferences is null)
        {
            return null;
        }

        try
        {
            return await _preferences.FindAsync(recipientId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A lookup failure must not lose the notification: deliver with the defaults.
            _logger.LogWarning(ex, "Notification preference lookup failed. RecipientId={RecipientId}", recipientId);
            return null;
        }
    }

    private async Task PushSafeAsync(Notification notification, bool generic, NotificationKind kind, CancellationToken ct)
    {
        if (_push is null)
        {
            return;
        }

        // Generic: a category-level sentence without names, in the recipient's language.
        var body = notification.Message;
        if (generic)
        {
            var lang = await LanguageOfAsync(notification.RecipientId, ct);
            body = Say(lang, $"push.generic.{kind.Category}");
        }

        // The device push repeats the stored, already-localized text and nothing more.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(PushBudget);

        try
        {
            await _push.DispatchAsync(
                notification.RecipientId,
                new PushPayload(
                    PushTitle,
                    body,
                    notification.Type,
                    notification.Id,
                    notification.PropertyId,
                    notification.RelatedEntityId,
                    NotificationKindCatalog.Describe(notification.Type).TargetKind,
                    NotificationKindCatalog.Describe(notification.Type).Category),
                budget.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Push delivery failed. NotificationId={NotificationId}",
                notification.Id);
        }
    }

    /// <summary>
    /// Tells every open connection of the user (other tabs, other devices) that notifications were read, so
    /// their lists and badges agree. <paramref name="ids"/> null means "all".
    /// </summary>
    private async Task BroadcastReadStateAsync(Guid userId, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        try
        {
            var unread = await _notifications.GetUnreadCountAsync(userId, ct);
            var group = _hub.Clients.Group(NotificationHub.GetGroupName(userId));
            await group.SendAsync(NotificationHub.UnreadCountChangedEvent, unread, ct);
            await group.SendAsync(NotificationHub.NotificationsReadEvent, new { ids, all = ids is null }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read-state broadcast failed. UserId={UserId}", userId);
        }
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
            CreatedAt = notification.CreatedAt,
            Category = NotificationKindCatalog.Describe(notification.Type).Category,
            Priority = notification.Priority,
            Target = NotificationKindCatalog.TargetFor(notification.Type, notification.PropertyId, notification.RelatedEntityId),
        };
    }
}
