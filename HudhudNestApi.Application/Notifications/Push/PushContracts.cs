using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Notifications.Push;

/// <summary>Already localized for the recipient. Never carries more than the in-app notification does.</summary>
public sealed record PushPayload(
    string Title,
    string Body,
    NotificationType Type,
    Guid NotificationId,
    Guid? PropertyId,
    Guid? RelatedEntityId,
    string? TargetKind = null,
    string? Category = null);

/// <summary>Fan-out of one notification to every device the recipient has registered.</summary>
public interface IPushNotificationDispatcher
{
    /// <summary>Best effort: a failed push must never fail the business operation that raised it.</summary>
    Task DispatchAsync(Guid recipientId, PushPayload payload, CancellationToken ct = default);
}

public enum PushSendStatus
{
    Sent,
    /// <summary>The provider says this token is dead (uninstalled app, rotated token): delete it.</summary>
    InvalidToken,
    /// <summary>Transient or configuration failure: keep the token, try again on the next notification.</summary>
    Failed,
    /// <summary>Push is not configured in this environment.</summary>
    Disabled,
}

/// <summary>One provider (FCM today, which also delivers to iOS through APNs).</summary>
public interface IPushSender
{
    Task<PushSendStatus> SendAsync(string deviceToken, PushPayload payload, CancellationToken ct = default);
}

public interface IDeviceTokenService
{
    /// <summary>
    /// Binds the token to this user. A token is a physical app install: if another account on the
    /// same device registered it before, it moves to the new account, so a notification for the
    /// previous account can never ring on this phone.
    /// </summary>
    Task RegisterAsync(Guid userId, string token, DevicePlatform platform, CancellationToken ct = default);

    /// <summary>Removes the token, but only if it belongs to this user.</summary>
    Task UnregisterAsync(Guid userId, string token, CancellationToken ct = default);

    /// <summary>
    /// Removes every token bound to this user. Account deletion calls it: a device token is an
    /// install identifier and must not outlive the person it identified.
    /// </summary>
    Task RemoveAllForUserAsync(Guid userId, CancellationToken ct = default);
}
