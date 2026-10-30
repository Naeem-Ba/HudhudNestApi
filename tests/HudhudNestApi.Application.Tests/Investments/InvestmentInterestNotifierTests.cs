using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Application.Investments.Services;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Tests.Investments;

/// <summary>QA Phase 10, P10-D01: a new expression of interest reaches the admins, and a failure never breaks it.</summary>
public sealed class InvestmentInterestNotifierTests
{
    [Fact]
    public async Task NotifyAdmins_SendsOneNoticePerAdmin_ExceptTheInterestedUser()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var otherAdmin = Guid.NewGuid();
        var interest = InvestmentInterest.Create(userId, projectId);

        var interests = new Mock<IInvestmentInterestRepository>();
        interests.Setup(x => x.ListAdminIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([userId, otherAdmin]);
        interests.Setup(x => x.CountActiveAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var notifications = new Mock<INotificationService>();

        var notifier = new InvestmentInterestNotifier(
            notifications.Object, interests.Object, NullLogger<InvestmentInterestNotifier>.Instance);

        await notifier.NotifyAdminsAsync(interest, "مشروع", CancellationToken.None);

        notifications.Verify(x => x.NotifyInvestmentInterestAsync(
            otherAdmin, It.IsAny<NotificationText>(), projectId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(x => x.NotifyInvestmentInterestAsync(
            userId, It.IsAny<NotificationText>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyAdmins_DoesNotThrow_WhenTheNotificationServiceFails()
    {
        var interest = InvestmentInterest.Create(Guid.NewGuid(), Guid.NewGuid());
        var interests = new Mock<IInvestmentInterestRepository>();
        interests.Setup(x => x.ListAdminIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Guid.NewGuid()]);
        var notifications = new Mock<INotificationService>();
        notifications.Setup(x => x.NotifyInvestmentInterestAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationText>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("push down"));

        var notifier = new InvestmentInterestNotifier(
            notifications.Object, interests.Object, NullLogger<InvestmentInterestNotifier>.Instance);

        await notifier.NotifyAdminsAsync(interest, "x", CancellationToken.None);
    }

    [Fact]
    public async Task NotifyAdmins_DoesNotThrow_WhenListingAdminsFails()
    {
        var interest = InvestmentInterest.Create(Guid.NewGuid(), Guid.NewGuid());
        var interests = new Mock<IInvestmentInterestRepository>();
        interests.Setup(x => x.ListAdminIdsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));

        var notifier = new InvestmentInterestNotifier(
            Mock.Of<INotificationService>(), interests.Object, NullLogger<InvestmentInterestNotifier>.Instance);

        await notifier.NotifyAdminsAsync(interest, "x", CancellationToken.None);
    }
}
