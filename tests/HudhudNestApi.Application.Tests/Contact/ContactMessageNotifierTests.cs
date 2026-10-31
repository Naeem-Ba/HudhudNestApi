using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Contact.Interfaces;
using HudhudNestApi.Application.Contact.Services;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Application.Tests.Contact;

/// <summary>QA Phase 12, P12-D10: a new contact message reaches every admin, and a failure never breaks the submit.</summary>
public sealed class ContactMessageNotifierTests
{
    private static ContactMessage NewMessage(string? subject = "استفسار") =>
        new() { Name = "نعيم", Email = "a@b.co", Subject = subject, Body = "نص" };

    [Fact]
    public async Task NotifyAdmins_SendsOneNoticePerAdmin_WithTheMessageIdAsTarget()
    {
        var admins = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var message = NewMessage();
        var repo = new Mock<IContactMessageRepository>();
        repo.Setup(x => x.ListAdminIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(admins);
        var notifications = new Mock<INotificationService>();

        var notifier = new ContactMessageNotifier(notifications.Object, repo.Object, NullLogger<ContactMessageNotifier>.Instance);
        await notifier.NotifyAdminsAsync(message, CancellationToken.None);

        foreach (var admin in admins)
            notifications.Verify(x => x.NotifyContactMessageAsync(
                admin, It.IsAny<NotificationText>(), message.Id, $"cmr:{message.Id:N}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyAdmins_UsesTheNoSubjectKey_WhenTheSubjectIsEmpty()
    {
        var repo = new Mock<IContactMessageRepository>();
        repo.Setup(x => x.ListAdminIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Guid.NewGuid()]);
        NotificationText? sent = null;
        var notifications = new Mock<INotificationService>();
        notifications.Setup(x => x.NotifyContactMessageAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationText>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NotificationText, Guid, string, CancellationToken>((_, t, _, _, _) => sent = t)
            .Returns(Task.CompletedTask);

        var notifier = new ContactMessageNotifier(notifications.Object, repo.Object, NullLogger<ContactMessageNotifier>.Instance);
        await notifier.NotifyAdminsAsync(NewMessage(subject: null), CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal(NotificationKeys.ContactMessageReceived, sent!.Key);
    }

    [Fact]
    public async Task NotifyAdmins_SendsNothing_WhenThereIsNoAdmin()
    {
        var repo = new Mock<IContactMessageRepository>();
        repo.Setup(x => x.ListAdminIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var notifications = new Mock<INotificationService>();

        var notifier = new ContactMessageNotifier(notifications.Object, repo.Object, NullLogger<ContactMessageNotifier>.Instance);
        await notifier.NotifyAdminsAsync(NewMessage(), CancellationToken.None);

        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NotifyAdmins_DoesNotThrow_WhenTheNotificationServiceFails()
    {
        var repo = new Mock<IContactMessageRepository>();
        repo.Setup(x => x.ListAdminIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Guid.NewGuid()]);
        var notifications = new Mock<INotificationService>();
        notifications.Setup(x => x.NotifyContactMessageAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationText>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("push down"));

        var notifier = new ContactMessageNotifier(notifications.Object, repo.Object, NullLogger<ContactMessageNotifier>.Instance);
        await notifier.NotifyAdminsAsync(NewMessage(), CancellationToken.None);
    }

    [Fact]
    public async Task NotifyAdmins_DoesNotThrow_WhenListingAdminsFails()
    {
        var repo = new Mock<IContactMessageRepository>();
        repo.Setup(x => x.ListAdminIdsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));

        var notifier = new ContactMessageNotifier(Mock.Of<INotificationService>(), repo.Object, NullLogger<ContactMessageNotifier>.Instance);
        await notifier.NotifyAdminsAsync(NewMessage(), CancellationToken.None);
    }
}
