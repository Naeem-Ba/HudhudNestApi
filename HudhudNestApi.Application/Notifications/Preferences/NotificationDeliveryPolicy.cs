using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Notifications.Preferences;

/// <summary>How a push should go out for one notification.</summary>
public enum PushDecision
{
    Skip,
    Generic,
    Full,
}

/// <summary>
/// The rules that turn a user's preferences into delivery decisions. Pure functions, so every rule is unit tested.
/// Mandatory events always go through. Absent preferences mean the defaults.
/// </summary>
public static class NotificationDeliveryPolicy
{
    /// <summary>Categories whose full text carries other people's names; a generic push text exists for each.</summary>
    public static readonly IReadOnlySet<string> NameBearingCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        NotificationKindCatalog.Messages,
        NotificationKindCatalog.Visits,
        NotificationKindCatalog.ShortStay,
        NotificationKindCatalog.Services,
        NotificationKindCatalog.Agency,
        NotificationKindCatalog.Social,
    };

    public static bool ShouldStore(NotificationPreference? prefs, NotificationKind kind) =>
        kind.IsMandatory || prefs is null || !prefs.MutedCategories.Contains(kind.Category);

    public static PushDecision DecidePush(NotificationPreference? prefs, NotificationKind kind, DateTime utcNow)
    {
        if (!kind.IsMandatory && prefs is not null)
        {
            if (prefs.PushMutedCategories.Contains(kind.Category))
            {
                return PushDecision.Skip;
            }

            if (kind.Priority != NotificationPriority.High && InQuietHours(prefs, utcNow))
            {
                return PushDecision.Skip;
            }
        }

        var level = prefs?.PushPreview ?? PushPreviewLevel.Generic;
        return level == PushPreviewLevel.Generic && NameBearingCategories.Contains(kind.Category)
            ? PushDecision.Generic
            : PushDecision.Full;
    }

    public static bool InQuietHours(NotificationPreference prefs, DateTime utcNow)
    {
        if (prefs.QuietStartMinute is not { } start || prefs.QuietEndMinute is not { } end || start == end)
        {
            return false;
        }

        var local = ToLocal(prefs.TimeZoneId, utcNow);
        var minute = local.Hour * 60 + local.Minute;
        return start < end
            ? minute >= start && minute < end
            : minute >= start || minute < end; // spans midnight, for example 22:00-07:00
    }

    private static DateTime ToLocal(string? zoneId, DateTime utcNow)
    {
        var utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        if (!string.IsNullOrWhiteSpace(zoneId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        }

        return utc;
    }

    public static bool IsKnownZone(string zoneId) => TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out _);
}
