using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Services.ProviderOnboarding;

/// <summary>
/// Best-effort notification after a decision is committed, same rule as the service-request
/// handlers: the decision is already saved, so a notification failure is logged and never turns
/// a successful approval into a 500 (or invites the admin to approve twice).
/// </summary>
internal static class ProviderOnboardingNotifier
{
    public static async Task TryNotifyAsync(
        INotificationService notifications,
        ILogger logger,
        Guid recipientId,
        NotificationType type,
        NotificationText message,
        Guid relatedEntityId,
        CancellationToken ct)
    {
        try
        {
            await notifications.NotifyServiceProviderUpdateAsync(recipientId, type, message, relatedEntityId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Service provider notification failed. Type={Type}, RelatedEntityId={RelatedEntityId}",
                type,
                relatedEntityId);
        }
    }
}
