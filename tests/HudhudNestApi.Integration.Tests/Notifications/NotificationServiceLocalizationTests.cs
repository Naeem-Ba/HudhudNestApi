using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Notifications.Push;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Notifications;

namespace HudhudNestApi.Integration.Tests.Notifications;

public sealed partial class NotificationServiceSignalRTests
{
    private sealed class FakeLanguages : IRecipientLanguageResolver
    {
        public Dictionary<Guid, string> ByUser { get; } = new();

        public Task<string> GetLanguageAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(ByUser.TryGetValue(userId, out var lang) ? lang : "ar");
    }

    private sealed class CapturingPush : IPushNotificationDispatcher
    {
        public List<(Guid Recipient, PushPayload Payload)> Sent { get; } = new();

        public Task DispatchAsync(Guid recipientId, PushPayload payload, CancellationToken ct = default)
        {
            Sent.Add((recipientId, payload));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPush : IPushNotificationDispatcher
    {
        public Task DispatchAsync(Guid recipientId, PushPayload payload, CancellationToken ct = default) =>
            throw new InvalidOperationException("push provider down");
    }

    private static (NotificationService Service, CapturingNotificationRepository Repo, FakeLanguages Languages, CapturingPush Push) Build()
    {
        var repo = new CapturingNotificationRepository();
        var languages = new FakeLanguages();
        var push = new CapturingPush();
        var service = new NotificationService(
            repo,
            new CapturingNotificationHubContext(),
            NullLogger<NotificationService>.Instance,
            languages,
            push);
        return (service, repo, languages, push);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task EachRecipientGetsTheirOwnLanguage_InTheSameProcess_RegardlessOfWhoTriggeredTheEvent()
    {
        var (service, repo, languages, _) = Build();
        var arabic = Guid.NewGuid();
        var german = Guid.NewGuid();
        var english = Guid.NewGuid();
        languages.ByUser[arabic] = "ar";
        languages.ByUser[german] = "de";
        languages.ByUser[english] = "en";
        var message = Guid.NewGuid();
        var property = Guid.NewGuid();

        // The same sender (whatever language they use) writes to three people.
        foreach (var recipient in new[] { arabic, german, english })
        {
            await service.NotifyNewMessageAsync(recipient, Guid.NewGuid(), "Anna", message, property);
        }

        Assert.Equal("أرسل لك Anna رسالة جديدة.", repo.Notifications.Single(n => n.RecipientId == arabic).Message);
        Assert.Equal("Anna hat Ihnen eine neue Nachricht gesendet.", repo.Notifications.Single(n => n.RecipientId == german).Message);
        Assert.Equal("Anna sent you a new message.", repo.Notifications.Single(n => n.RecipientId == english).Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task ChangingTheRecipientsLanguage_AffectsOnlyLaterNotifications()
    {
        var (service, repo, languages, _) = Build();
        var user = Guid.NewGuid();
        languages.ByUser[user] = "ar";

        await service.NotifyUserRatedAsync(user, Guid.NewGuid(), "Sam", 4.5);
        languages.ByUser[user] = "de";
        await service.NotifyUserRatedAsync(user, Guid.NewGuid(), "Sam", 4.5);

        Assert.Equal("قيّمك Sam بمتوسط 4.5/5 على صفحتك الشخصية.", repo.Notifications[0].Message);
        Assert.Equal("Sam hat Sie auf Ihrem Profil mit durchschnittlich 4.5/5 bewertet.", repo.Notifications[1].Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task DetailAndOuterSentence_AreBothRenderedInTheRecipientsLanguage_AndUserTextIsKeptVerbatim()
    {
        var (service, repo, languages, _) = Build();
        var owner = Guid.NewGuid();
        languages.ByUser[owner] = "de";

        await service.NotifyPropertyUpdateAsync(
            owner, Guid.NewGuid(), "Altbau {detail} am Park", NotificationType.VisitRequested,
            NotificationText.Of(
                NotificationKeys.VisitRequestedDetail,
                ("name", "Layla"),
                ("at", new DateTime(2027, 1, 2, 10, 30, 0)),
                ("note", NotificationText.Of(NotificationKeys.NoteFromVisitor, ("note", "Bitte klingeln")))));

        Assert.Equal(
            "Neue Besichtigungsanfrage für Ihre Immobilie 'Altbau {detail} am Park': Layla am 02/01/2027 10:30. — Hinweis des Besuchers: Bitte klingeln",
            Assert.Single(repo.Notifications).Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task AnAccountThatNeverChoseALanguage_KeepsTheLegacyArabicWording()
    {
        var (service, repo, _, _) = Build(); // resolver returns the fallback for unknown users

        await service.NotifyValuationResultReadyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.StartsWith("اكتمل تقييم عقارك", Assert.Single(repo.Notifications).Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task Push_CarriesTheSameLocalizedText_ToTheRecipientOnly()
    {
        var (service, repo, languages, push) = Build();
        var guest = Guid.NewGuid();
        languages.ByUser[guest] = "en";

        await service.NotifyShortStayBookingUpdateAsync(
            guest, Guid.NewGuid(), "Villa Rosa", NotificationType.ShortStayBookingConfirmed,
            NotificationText.Of(NotificationKeys.ShortStayConfirmedDetail, ("title", "Villa Rosa")));

        var (recipient, payload) = Assert.Single(push.Sent);
        Assert.Equal(guest, recipient);
        Assert.Equal("Your booking for 'Villa Rosa' was confirmed.", payload.Body);
        Assert.Equal(repo.Notifications.Single().Message, payload.Body);
        Assert.Equal(NotificationType.ShortStayBookingConfirmed, payload.Type);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task APushProviderFailure_DoesNotFailOrLoseTheNotification()
    {
        var repo = new CapturingNotificationRepository();
        var service = new NotificationService(
            repo,
            new CapturingNotificationHubContext(),
            NullLogger<NotificationService>.Instance,
            new FakeLanguages(),
            new ThrowingPush());

        await service.NotifyValuationResultReadyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Single(repo.Notifications);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task ABlankName_BecomesATranslatedPlaceholder_NotAnArabicOne()
    {
        var (service, repo, languages, _) = Build();
        var user = Guid.NewGuid();
        languages.ByUser[user] = "en";

        await service.NotifyAgencyInvitationRespondedAsync(user, Guid.NewGuid(), "  ", accepted: true);

        Assert.Equal("The user accepted the invitation to join your agency.", Assert.Single(repo.Notifications).Message);
    }
}
