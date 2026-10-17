using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Integration.Tests.Notifications;

public sealed partial class NotificationServiceSignalRTests
{
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task NotifyingTheSameMessageTwice_StoresAndPushesOnlyOnce()
    {
        var (service, repo, _, push) = Build();
        var recipient = Guid.NewGuid();
        var message = Guid.NewGuid();
        var property = Guid.NewGuid();

        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", message, property);
        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", message, property);

        Assert.Single(repo.Notifications);
        Assert.Single(push.Sent);
        Assert.Equal($"msg:{message:N}", repo.Notifications[0].DedupeKey);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task DifferentMessages_AreNotDeduplicated()
    {
        var (service, repo, _, push) = Build();
        var recipient = Guid.NewGuid();
        var property = Guid.NewGuid();

        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", Guid.NewGuid(), property);
        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", Guid.NewGuid(), property);

        Assert.Equal(2, repo.Notifications.Count);
        Assert.Equal(2, push.Sent.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task RepeatedValuationReminder_ForTheSameInquiry_IsStoredOnce()
    {
        var (service, repo, _, _) = Build();
        var recipient = Guid.NewGuid();
        var inquiry = Guid.NewGuid();

        await service.NotifyValuationInquiryReminderSoonAsync(recipient, inquiry);
        await service.NotifyValuationInquiryReminderSoonAsync(recipient, inquiry);

        Assert.Single(repo.Notifications);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task StoredNotification_CarriesPriorityFromTheCatalog()
    {
        var (service, repo, _, _) = Build();
        var recipient = Guid.NewGuid();

        await service.NotifyListingExpiredAsync(recipient, Guid.NewGuid(), "Flat", 7);
        await service.NotifyUserRatedAsync(recipient, Guid.NewGuid(), "Sam", 4.5);

        Assert.Equal(NotificationPriority.High, repo.Notifications.Single(n => n.Type == NotificationType.ListingExpired).Priority);
        Assert.Equal(NotificationPriority.Low, repo.Notifications.Single(n => n.Type == NotificationType.UserRated).Priority);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task MarkingRead_BroadcastsToTheWholeUserGroup_NotJustTheCaller()
    {
        var hub = new CapturingNotificationHubContext();
        var repo = new CapturingNotificationRepository();
        var service = new HudhudNestApi.Infrastructure.Notifications.NotificationService(
            repo, hub, Microsoft.Extensions.Logging.Abstractions.NullLogger<HudhudNestApi.Infrastructure.Notifications.NotificationService>.Instance);
        var recipient = Guid.NewGuid();
        await service.NotifyUserRatedAsync(recipient, Guid.NewGuid(), "Sam", 4.5);
        var id = repo.Notifications.Single().Id;

        await service.MarkAsReadAsync(id, recipient);

        // The last event of the pair is NotificationsRead; both go to the user's whole group.
        Assert.Equal("NotificationsRead", hub.Clients.LastMethodName);
        Assert.Equal($"user_{recipient:D}", hub.Clients.LastGroupName);
    }

    private sealed class FakePrefs : HudhudNestApi.Application.Notifications.Preferences.INotificationPreferenceStore
    {
        public Dictionary<Guid, HudhudNestApi.Domain.Notifications.Entities.NotificationPreference> Rows { get; } = new();

        public Task<HudhudNestApi.Domain.Notifications.Entities.NotificationPreference?> FindAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Rows.TryGetValue(userId, out var row) ? row : null);

        public Task SaveAsync(HudhudNestApi.Domain.Notifications.Entities.NotificationPreference preference, CancellationToken ct = default)
        {
            Rows[preference.UserId] = preference;
            return Task.CompletedTask;
        }
    }

    private static (HudhudNestApi.Infrastructure.Notifications.NotificationService Service, CapturingNotificationRepository Repo, CapturingPush Push, FakePrefs Prefs, FakeLanguages Languages) BuildWithPrefs()
    {
        var repo = new CapturingNotificationRepository();
        var push = new CapturingPush();
        var prefs = new FakePrefs();
        var languages = new FakeLanguages();
        var service = new HudhudNestApi.Infrastructure.Notifications.NotificationService(
            repo,
            new CapturingNotificationHubContext(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HudhudNestApi.Infrastructure.Notifications.NotificationService>.Instance,
            languages,
            push,
            prefs);
        return (service, repo, push, prefs, languages);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task ByDefault_ThePushText_HasNoSenderName()
    {
        var (service, repo, push, _, _) = BuildWithPrefs();
        var recipient = Guid.NewGuid();

        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna Schmidt", Guid.NewGuid(), Guid.NewGuid());

        Assert.Contains("Anna Schmidt", repo.Notifications.Single().Message); // the in-app text keeps the name
        var sent = push.Sent.Single().Payload;
        Assert.DoesNotContain("Anna", sent.Body);
        Assert.Equal("لديك رسالة جديدة بخصوص عقار تتابعه", sent.Body);
        Assert.Equal("messages", sent.TargetKind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task FullPreview_AndTheRecipientsLanguage_AreHonoured()
    {
        var (service, _, push, prefs, languages) = BuildWithPrefs();
        var recipient = Guid.NewGuid();
        languages.ByUser[recipient] = "en";
        prefs.Rows[recipient] = new() { UserId = recipient, PushPreview = HudhudNestApi.Domain.Notifications.Entities.PushPreviewLevel.Full };

        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("Anna sent you a new message.", push.Sent.Single().Payload.Body);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task GenericPushText_FollowsTheRecipientsLanguage()
    {
        var (service, _, push, _, languages) = BuildWithPrefs();
        var recipient = Guid.NewGuid();
        languages.ByUser[recipient] = "de";

        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("Sie haben eine neue Nachricht zu einer Immobilie, die Sie verfolgen", push.Sent.Single().Payload.Body);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task MutedCategory_StoresNothingAndSendsNothing_ButMandatoryStillArrives()
    {
        var (service, repo, push, prefs, _) = BuildWithPrefs();
        var recipient = Guid.NewGuid();
        prefs.Rows[recipient] = new() { UserId = recipient, MutedCategories = { "messages", "listings" } };

        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", Guid.NewGuid(), Guid.NewGuid());
        await service.NotifyListingExpiredAsync(recipient, Guid.NewGuid(), "Flat", 7);

        Assert.Single(repo.Notifications);
        Assert.Equal(NotificationType.ListingExpired, repo.Notifications[0].Type);
        Assert.Single(push.Sent);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task PushMutedCategory_KeepsTheInAppCopy_ButSendsNoPush()
    {
        var (service, repo, push, prefs, _) = BuildWithPrefs();
        var recipient = Guid.NewGuid();
        prefs.Rows[recipient] = new() { UserId = recipient, PushMutedCategories = { "messages" } };

        await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", Guid.NewGuid(), Guid.NewGuid());

        Assert.Single(repo.Notifications);
        Assert.Empty(push.Sent);
    }
}
