using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Models;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Hubs;
using HudhudNestApi.Infrastructure.Notifications;

namespace HudhudNestApi.Infrastructure.Tests.Notifications;

public sealed class NotificationLocalizationTests
{
    private static readonly NotificationMessageContext Context =
        new("Layla", "Omar", "Sea view flat", "XYZ Real Estate");

    [Theory(DisplayName = "NewMessage text follows the site language")]
    [InlineData("ar", "أرسل لك Layla رسالة جديدة.")]
    [InlineData("en", "Layla sent you a new message.")]
    [InlineData("de", "Layla hat Ihnen eine neue Nachricht gesendet.")]
    [InlineData("AR-SA", "أرسل لك Layla رسالة جديدة.")]
    [InlineData("en_US", "Layla sent you a new message.")]
    public void NewMessage_PerLanguage(string language, string expected) =>
        Assert.Equal(expected, NotificationTexts.Render(NotificationType.NewMessage, language, Context));

    [Theory(DisplayName = "Agency inquiry text names customer, member, listing and agency in every language")]
    [InlineData("ar")]
    [InlineData("en")]
    [InlineData("de")]
    public void AgencyInquiry_NamesEverybody(string language)
    {
        var text = NotificationTexts.Render(NotificationType.AgencyMemberInquiry, language, Context);

        Assert.NotNull(text);
        foreach (var part in new[] { "Layla", "Omar", "Sea view flat", "XYZ Real Estate" })
            Assert.Contains(part, text);
    }

    [Fact(DisplayName = "The three languages produce three different texts")]
    public void AgencyInquiry_TextsDiffer()
    {
        var texts = new[] { "ar", "en", "de" }
            .Select(l => NotificationTexts.Render(NotificationType.AgencyMemberInquiry, l, Context))
            .ToList();

        Assert.Equal(3, texts.Distinct().Count());
    }

    [Theory(DisplayName = "Missing names fall back to a localized placeholder, never to an empty or Arabic-only string")]
    [InlineData("en", "A user")]
    [InlineData("de", "Ein Nutzer")]
    [InlineData("ar", "مستخدم")]
    public void MissingNames_UseLocalizedFallback(string language, string expectedFallback)
    {
        var text = NotificationTexts.Render(
            NotificationType.NewMessage, language, new NotificationMessageContext(null, null, null, null));

        Assert.Contains(expectedFallback, text);
    }

    [Theory(DisplayName = "Unsupported / missing language and untemplated types keep the stored text (null)")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    public void UnsupportedLanguage_ReturnsNull(string? language) =>
        Assert.Null(NotificationTexts.Render(NotificationType.NewMessage, language, Context));

    [Fact(DisplayName = "A notification type without a template is left alone")]
    public void UntemplatedType_ReturnsNull() =>
        Assert.Null(NotificationTexts.Render(NotificationType.PropertyPublished, "en", Context));

    // ---- through the service ----------------------------------------------------------------

    private static Notification Stored(NotificationType type, Guid messageId, Guid recipient) => new()
    {
        RecipientId = recipient,
        Type = type,
        Message = "نص مخزَّن",
        PropertyId = Guid.NewGuid(),
        RelatedEntityId = messageId,
        CreatedAt = DateTime.UtcNow,
    };

    private static NotificationService Service(Mock<INotificationRepository> repo) =>
        new(repo.Object, Mock.Of<IHubContext<NotificationHub>>(), NullLogger<NotificationService>.Instance);

    private static Mock<INotificationRepository> Repo(Guid recipient, Guid messageId, params NotificationType[] types)
    {
        var repo = new Mock<INotificationRepository>();
        repo.Setup(x => x.GetUserNotificationsAsync(recipient, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(types.Select(t => Stored(t, messageId, recipient)).ToList());
        repo.Setup(x => x.GetMessageContextsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, NotificationMessageContext> { [messageId] = Context });
        return repo;
    }

    [Fact(DisplayName = "Service renders message notifications in the requested language, others unchanged")]
    public async Task Service_Localizes_MessageNotifications()
    {
        var recipient = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var repo = Repo(recipient, messageId, NotificationType.AgencyMemberInquiry, NotificationType.PropertyPublished);

        var result = await Service(repo).GetUserNotificationsAsync(recipient, 1, 20, "de");

        Assert.Contains("Neue Anfrage", result[0].Message);
        Assert.Equal("نص مخزَّن", result[1].Message);
    }

    [Fact(DisplayName = "Service without a language returns the stored text and does not query contexts")]
    public async Task Service_NoLanguage_KeepsStoredText()
    {
        var recipient = Guid.NewGuid();
        var repo = Repo(recipient, Guid.NewGuid(), NotificationType.NewMessage);

        var result = await Service(repo).GetUserNotificationsAsync(recipient, 1, 20);

        Assert.Equal("نص مخزَّن", Assert.Single(result).Message);
        repo.Verify(x => x.GetMessageContextsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "A failing context lookup never hides the list: stored texts are returned")]
    public async Task Service_ContextFailure_KeepsStoredText()
    {
        var recipient = Guid.NewGuid();
        var repo = Repo(recipient, Guid.NewGuid(), NotificationType.NewMessage);
        repo.Setup(x => x.GetMessageContextsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Service(repo).GetUserNotificationsAsync(recipient, 1, 20, "en");

        Assert.Equal("نص مخزَّن", Assert.Single(result).Message);
    }

    [Fact(DisplayName = "A message that no longer resolves keeps the stored text")]
    public async Task Service_UnresolvedMessage_KeepsStoredText()
    {
        var recipient = Guid.NewGuid();
        var repo = Repo(recipient, Guid.NewGuid(), NotificationType.NewMessage);
        repo.Setup(x => x.GetMessageContextsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, NotificationMessageContext>());

        var result = await Service(repo).GetUserNotificationsAsync(recipient, 1, 20, "en");

        Assert.Equal("نص مخزَّن", Assert.Single(result).Message);
    }
}
