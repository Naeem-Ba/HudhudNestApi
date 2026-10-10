using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Domain.Notifications.Entities;

/// <summary>
/// A push-notification address: one app install of one user. Not a <c>BaseEntity</c> on purpose --
/// a token carries no business history, so it is hard-deleted the moment it is revoked or the
/// provider reports it dead.
/// </summary>
public sealed class DeviceToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; }

    /// <summary>Provider registration token (FCM). Unique across all users.</summary>
    public string Token { get; private set; } = string.Empty;

    public DevicePlatform Platform { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public static DeviceToken Create(Guid userId, string token, DevicePlatform platform, DateTime utcNow)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token is required.", nameof(token));
        }

        return new DeviceToken
        {
            UserId = userId,
            Token = token.Trim(),
            Platform = platform,
            CreatedAt = utcNow,
            LastSeenAt = utcNow,
        };
    }

    /// <summary>Re-registration (app start, token refresh, or another account on the same phone).</summary>
    public void Rebind(Guid userId, DevicePlatform platform, DateTime utcNow)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        UserId = userId;
        Platform = platform;
        LastSeenAt = utcNow;
    }
}
