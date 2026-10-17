using HudhudNestApi.Application.Notifications.Preferences;
using HudhudNestApi.Domain.Notifications.Entities;

namespace HudhudNestApi.Application.Tests.Notifications;

public sealed class NotificationPreferenceServiceTests
{
    private sealed class FakeStore : INotificationPreferenceStore
    {
        public Dictionary<Guid, NotificationPreference> Rows { get; } = new();

        public Task<NotificationPreference?> FindAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Rows.TryGetValue(userId, out var row) ? row : null);

        public Task SaveAsync(NotificationPreference preference, CancellationToken ct = default)
        {
            Rows[preference.UserId] = preference;
            return Task.CompletedTask;
        }
    }

    private static (NotificationPreferenceService Service, FakeStore Store) Build()
    {
        var store = new FakeStore();
        return (new NotificationPreferenceService(store), store);
    }

    [Fact]
    public async Task NeverSaved_ReturnsDefaults_AndTheCategoryList()
    {
        var (service, store) = Build();

        var dto = await service.GetAsync(Guid.NewGuid());

        Assert.Empty(dto.MutedCategories);
        Assert.Equal(PushPreviewLevel.Generic, dto.PushPreview);
        Assert.Contains(dto.Categories, c => c.Id == "messages" && c.Mutable);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task Update_SavesOnlyTheUsersOwnRow()
    {
        var (service, store) = Build();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();

        var dto = await service.UpdateAsync(me, new UpdateNotificationPreferencesRequest
        {
            MutedCategories = new() { "social" },
            PushPreview = PushPreviewLevel.Full,
        });

        Assert.Equal(new[] { "social" }, dto.MutedCategories);
        Assert.True(store.Rows.ContainsKey(me));
        Assert.False(store.Rows.ContainsKey(other));
    }

    [Fact]
    public async Task UnknownOrMandatoryOnlyCategory_IsRejected()
    {
        var (service, _) = Build();

        await Assert.ThrowsAsync<NotificationPreferenceValidationException>(() =>
            service.UpdateAsync(Guid.NewGuid(), new UpdateNotificationPreferencesRequest { MutedCategories = new() { "nope" } }));
        // "account" holds only mandatory types, so it cannot be switched off.
        await Assert.ThrowsAsync<NotificationPreferenceValidationException>(() =>
            service.UpdateAsync(Guid.NewGuid(), new UpdateNotificationPreferencesRequest { PushMutedCategories = new() { "account" } }));
    }

    [Fact]
    public async Task QuietHours_AreValidated()
    {
        var (service, _) = Build();
        var user = Guid.NewGuid();

        await Assert.ThrowsAsync<NotificationPreferenceValidationException>(() =>
            service.UpdateAsync(user, new UpdateNotificationPreferencesRequest { QuietHoursEnabled = true, QuietStartMinute = 100 }));
        await Assert.ThrowsAsync<NotificationPreferenceValidationException>(() =>
            service.UpdateAsync(user, new UpdateNotificationPreferencesRequest { QuietStartMinute = 100, QuietEndMinute = 100 }));
        await Assert.ThrowsAsync<NotificationPreferenceValidationException>(() =>
            service.UpdateAsync(user, new UpdateNotificationPreferencesRequest { QuietStartMinute = 2000, QuietEndMinute = 100 }));
        await Assert.ThrowsAsync<NotificationPreferenceValidationException>(() =>
            service.UpdateAsync(user, new UpdateNotificationPreferencesRequest { TimeZoneId = "Not/AZone" }));

        var ok = await service.UpdateAsync(user, new UpdateNotificationPreferencesRequest
        {
            QuietHoursEnabled = true,
            QuietStartMinute = 22 * 60,
            QuietEndMinute = 7 * 60,
            TimeZoneId = "Europe/Berlin",
        });
        Assert.Equal(22 * 60, ok.QuietStartMinute);

        var off = await service.UpdateAsync(user, new UpdateNotificationPreferencesRequest { QuietHoursEnabled = false });
        Assert.Null(off.QuietStartMinute);
        Assert.Null(off.QuietEndMinute);
    }

    [Fact]
    public async Task OmittedFields_AreLeftAlone()
    {
        var (service, _) = Build();
        var user = Guid.NewGuid();
        await service.UpdateAsync(user, new UpdateNotificationPreferencesRequest { MutedCategories = new() { "social" } });

        var dto = await service.UpdateAsync(user, new UpdateNotificationPreferencesRequest { PushPreview = PushPreviewLevel.Full });

        Assert.Equal(new[] { "social" }, dto.MutedCategories);
        Assert.Equal(PushPreviewLevel.Full, dto.PushPreview);
    }
}
