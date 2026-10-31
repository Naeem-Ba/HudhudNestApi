using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Plans.Services;

/// <summary>
/// QA Phase 13 (P13-D03): there is no payment gateway, so the user pays by hand and used to hear nothing back. This tells
/// them what became of a plan request or listing fee and when their plan ends. Called after the decision is committed and
/// never throws: a notice that fails is logged and the decision stays.
/// </summary>
public sealed class BillingNotifier
{
    private readonly INotificationService _notifications;
    private readonly ILogger<BillingNotifier> _logger;

    public BillingNotifier(INotificationService notifications, ILogger<BillingNotifier> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public Task PlanRequestApprovedAsync(Guid userId, Guid requestId, string tier, DateTime? expiresAt, CancellationToken ct)
        => SendAsync(userId, NotificationType.PlanRequestApproved, requestId, $"plan-approved:{requestId:N}",
            NotificationText.Of(NotificationKeys.PlanRequestApproved,
                ("plan", NotificationKeys.PlanName(tier)),
                ("until", expiresAt)),
            ct);

    public Task PlanRequestRejectedAsync(Guid userId, Guid requestId, string tier, string? reason, CancellationToken ct)
    {
        var plan = NotificationKeys.PlanName(tier);
        var text = string.IsNullOrWhiteSpace(reason)
            ? NotificationText.Of(NotificationKeys.PlanRequestRejected, ("plan", plan))
            : NotificationText.Of(NotificationKeys.PlanRequestRejectedWithReason,
                ("plan", plan), ("reason", NotificationText.FromLiteral(reason.Trim())));
        return SendAsync(userId, NotificationType.PlanRequestRejected, requestId, $"plan-rejected:{requestId:N}", text, ct);
    }

    public Task ListingExtensionConfirmedAsync(Guid userId, Guid transactionId, string title, DateTime expiresAt, CancellationToken ct)
        => SendAsync(userId, NotificationType.ListingFeeConfirmed, transactionId, $"fee-confirmed:{transactionId:N}",
            NotificationText.Of(NotificationKeys.ListingFeeConfirmedExtension,
                ("title", NotificationText.FromLiteral(title)), ("until", expiresAt)),
            ct);

    public Task ListingFeaturedConfirmedAsync(Guid userId, Guid transactionId, string title, DateTime featuredUntil, CancellationToken ct)
        => SendAsync(userId, NotificationType.ListingFeeConfirmed, transactionId, $"fee-confirmed:{transactionId:N}",
            NotificationText.Of(NotificationKeys.ListingFeeConfirmedFeatured,
                ("title", NotificationText.FromLiteral(title)), ("until", featuredUntil)),
            ct);

    public Task ListingFeeRejectedAsync(Guid userId, Guid transactionId, bool isFeatured, string title, string? reason, CancellationToken ct)
    {
        var kind = NotificationText.Of(isFeatured ? NotificationKeys.FeeKindFeatured : NotificationKeys.FeeKindExtension);
        var titleText = NotificationText.FromLiteral(title);
        var text = string.IsNullOrWhiteSpace(reason)
            ? NotificationText.Of(NotificationKeys.ListingFeeRejected, ("kind", kind), ("title", titleText))
            : NotificationText.Of(NotificationKeys.ListingFeeRejectedWithReason,
                ("kind", kind), ("title", titleText), ("reason", NotificationText.FromLiteral(reason.Trim())));
        return SendAsync(userId, NotificationType.ListingFeeRejected, transactionId, $"fee-rejected:{transactionId:N}", text, ct);
    }

    /// <summary>One notice per plan end date, so a renewal that moves the date earns a fresh warning.</summary>
    public Task PlanExpiringSoonAsync(Guid userId, string tier, DateTime expiresAt, CancellationToken ct)
        => SendAsync(userId, NotificationType.PlanExpiringSoon, null, $"plan-expiring:{userId:N}:{expiresAt:yyyyMMddHHmm}",
            NotificationText.Of(NotificationKeys.PlanExpiringSoon,
                ("plan", NotificationKeys.PlanName(tier)), ("until", expiresAt)),
            ct);

    public Task PlanExpiredAsync(Guid userId, string tier, DateTime expiredAt, CancellationToken ct)
        => SendAsync(userId, NotificationType.PlanExpired, null, $"plan-expired:{userId:N}:{expiredAt:yyyyMMddHHmm}",
            NotificationText.Of(NotificationKeys.PlanExpired, ("plan", NotificationKeys.PlanName(tier))),
            ct);

    private async Task SendAsync(
        Guid userId, NotificationType type, Guid? relatedEntityId, string dedupeKey, NotificationText text, CancellationToken ct)
    {
        try
        {
            await _notifications.NotifyBillingAsync(userId, type, text, relatedEntityId, dedupeKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Billing notification failed. UserId={UserId}, Type={Type}, RelatedEntityId={RelatedEntityId}",
                userId, type, relatedEntityId);
        }
    }
}
