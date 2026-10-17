namespace HudhudNestApi.Domain.Notifications.Entities;

/// <summary>What a push shows on the lock screen.</summary>
public enum PushPreviewLevel
{
    /// <summary>A category-level sentence with no names ("You have a new message about a property you follow"). Default.</summary>
    Generic = 0,

    /// <summary>The same sentence the in-app notification shows.</summary>
    Full = 1,
}

/// <summary>
/// One row per user, created on first save. No row means the defaults: everything on, generic push text,
/// no quiet hours. Mandatory events (listing expiry, booking decisions, account verification...) ignore the
/// mute lists and quiet hours; see <c>NotificationKindCatalog</c>.
/// </summary>
public sealed class NotificationPreference
{
    public Guid UserId { get; set; }

    /// <summary>Categories the user does not want at all (no stored notification, no push).</summary>
    public List<string> MutedCategories { get; set; } = new();

    /// <summary>Categories that still appear in the app but never ring the phone.</summary>
    public List<string> PushMutedCategories { get; set; } = new();

    public PushPreviewLevel PushPreview { get; set; } = PushPreviewLevel.Generic;

    /// <summary>Quiet hours as minutes after local midnight; both null = off. Low and Normal pushes are held back.</summary>
    public int? QuietStartMinute { get; set; }

    public int? QuietEndMinute { get; set; }

    /// <summary>IANA zone id (for example "Europe/Berlin") the quiet hours are read in. Null/unknown = UTC.</summary>
    public string? TimeZoneId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
