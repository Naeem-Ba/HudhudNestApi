using HudhudNestApi.Domain.Notifications.Entities;

namespace HudhudNestApi.Application.Notifications.Preferences;

public sealed class NotificationPreferencesDto
{
    public List<string> MutedCategories { get; init; } = new();
    public List<string> PushMutedCategories { get; init; } = new();
    public PushPreviewLevel PushPreview { get; init; } = PushPreviewLevel.Generic;
    public int? QuietStartMinute { get; init; }
    public int? QuietEndMinute { get; init; }
    public string? TimeZoneId { get; init; }

    /// <summary>Every category, and whether the user may switch it off (false = only mandatory events).</summary>
    public IReadOnlyList<NotificationCategoryInfo> Categories { get; init; } = Array.Empty<NotificationCategoryInfo>();
}

public sealed record NotificationCategoryInfo(string Id, bool Mutable);

public sealed class UpdateNotificationPreferencesRequest
{
    public List<string>? MutedCategories { get; init; }
    public List<string>? PushMutedCategories { get; init; }
    public PushPreviewLevel? PushPreview { get; init; }
    public bool? QuietHoursEnabled { get; init; }
    public int? QuietStartMinute { get; init; }
    public int? QuietEndMinute { get; init; }
    public string? TimeZoneId { get; init; }
}

public interface INotificationPreferenceStore
{
    /// <summary>Null when the user never saved preferences (the defaults apply).</summary>
    Task<NotificationPreference?> FindAsync(Guid userId, CancellationToken ct = default);

    Task SaveAsync(NotificationPreference preference, CancellationToken ct = default);
}

/// <summary>Validation failure with a client-safe message.</summary>
public sealed class NotificationPreferenceValidationException : Exception
{
    public NotificationPreferenceValidationException(string message) : base(message)
    {
    }
}
