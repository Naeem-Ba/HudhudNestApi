using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Notifications.Preferences;

/// <summary>Reads and validates a user's notification preferences. The user id always comes from the token.</summary>
public sealed class NotificationPreferenceService
{
    private readonly INotificationPreferenceStore _store;

    public NotificationPreferenceService(INotificationPreferenceStore store)
    {
        _store = store;
    }

    /// <summary>Category id -> may the user switch it off. A category is mutable if any of its types is not mandatory.</summary>
    public static IReadOnlyList<NotificationCategoryInfo> Categories() =>
        Enum.GetValues<NotificationType>()
            .Select(NotificationKindCatalog.Describe)
            .GroupBy(k => k.Category)
            .Select(g => new NotificationCategoryInfo(g.Key, g.Any(k => !k.IsMandatory)))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

    public async Task<NotificationPreferencesDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await _store.FindAsync(userId, ct);
        return ToDto(row);
    }

    public async Task<NotificationPreferencesDto> UpdateAsync(
        Guid userId, UpdateNotificationPreferencesRequest request, CancellationToken ct = default)
    {
        var row = await _store.FindAsync(userId, ct) ?? new NotificationPreference { UserId = userId };
        var mutable = Categories().Where(c => c.Mutable).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        if (request.MutedCategories is not null)
        {
            row.MutedCategories = CleanCategories(request.MutedCategories, mutable);
        }

        if (request.PushMutedCategories is not null)
        {
            row.PushMutedCategories = CleanCategories(request.PushMutedCategories, mutable);
        }

        if (request.PushPreview is { } preview)
        {
            if (!Enum.IsDefined(preview))
            {
                throw new NotificationPreferenceValidationException("Unknown push preview level.");
            }

            row.PushPreview = preview;
        }

        if (request.TimeZoneId is not null)
        {
            var zone = request.TimeZoneId.Trim();
            if (zone.Length > 64 || (zone.Length > 0 && !NotificationDeliveryPolicy.IsKnownZone(zone)))
            {
                throw new NotificationPreferenceValidationException("Unknown time zone.");
            }

            row.TimeZoneId = zone.Length == 0 ? null : zone;
        }

        if (request.QuietHoursEnabled is false)
        {
            row.QuietStartMinute = null;
            row.QuietEndMinute = null;
        }
        else if (request.QuietHoursEnabled is true || request.QuietStartMinute is not null || request.QuietEndMinute is not null)
        {
            var start = request.QuietStartMinute ?? row.QuietStartMinute;
            var end = request.QuietEndMinute ?? row.QuietEndMinute;
            if (start is null || end is null || start is < 0 or > 1439 || end is < 0 or > 1439 || start == end)
            {
                throw new NotificationPreferenceValidationException("Quiet hours need two different times of day.");
            }

            row.QuietStartMinute = start;
            row.QuietEndMinute = end;
        }

        row.UpdatedAt = DateTime.UtcNow;
        await _store.SaveAsync(row, ct);
        return ToDto(row);
    }

    private static List<string> CleanCategories(IEnumerable<string> input, HashSet<string> mutable)
    {
        var result = new List<string>();
        foreach (var raw in input.Take(32))
        {
            var id = raw?.Trim() ?? string.Empty;
            if (!mutable.Contains(id))
            {
                throw new NotificationPreferenceValidationException($"Category '{id}' cannot be switched off.");
            }

            if (!result.Contains(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static NotificationPreferencesDto ToDto(NotificationPreference? row) => new()
    {
        MutedCategories = row?.MutedCategories.ToList() ?? new(),
        PushMutedCategories = row?.PushMutedCategories.ToList() ?? new(),
        PushPreview = row?.PushPreview ?? PushPreviewLevel.Generic,
        QuietStartMinute = row?.QuietStartMinute,
        QuietEndMinute = row?.QuietEndMinute,
        TimeZoneId = row?.TimeZoneId,
        Categories = Categories(),
    };
}
