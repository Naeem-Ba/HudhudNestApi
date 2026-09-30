using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Admin.Services;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Admin;

public sealed class AdminSubscriptionServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    [Fact]
    public async Task ActivatePlanAsync_WithKnownTierAndValidDuration_ActivatesAndAuditLogs()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var plan = BuildPlan("premium");

        var accounts = FakeAccounts(userId, account);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByTierAsync("premium", It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildService(accounts.Object, plans.Object, Mock.Of<IUnitOfWork>(), auditLogs.Object);

        var result = await service.ActivatePlanAsync(
            userId, "premium", 30, "manual grant", AdminId, "127.0.0.1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(plan.Id, account.PlanId);
        Assert.NotNull(account.PlanExpiresAt);
        auditLogs.Verify(x => x.LogAsync(
            AdminId,
            AuditActions.PlanActivatedByAdmin,
            "127.0.0.1",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivatePlanAsync_WithUnknownTier_ReturnsBadRequest_AndDoesNotMutate()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);

        var accounts = FakeAccounts(userId, account);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByTierAsync("bogus", It.IsAny<CancellationToken>())).ReturnsAsync((Plan?)null);

        var service = BuildService(accounts.Object, plans.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.ActivatePlanAsync(
            userId, "bogus", 30, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(account.PlanId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3651)]
    public async Task ActivatePlanAsync_WithOutOfRangeDuration_ReturnsBadRequest(int days)
    {
        var userId = Guid.NewGuid();
        var service = BuildService(
            Mock.Of<IUserAccountRepository>(), Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.ActivatePlanAsync(
            userId, "premium", days, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExtendSubscriptionAsync_WhenActiveAndFuture_AddsOnTopOfCurrentExpiry_AndAuditLogs()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var planId = Guid.NewGuid();
        account.ActivatePlanByAdmin(planId, DateTime.UtcNow.AddDays(10), Guid.NewGuid(), DateTime.UtcNow);
        var expiryBeforeExtend = account.PlanExpiresAt!.Value;

        var accounts = FakeAccounts(userId, account);
        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), auditLogs.Object);

        var result = await service.ExtendSubscriptionAsync(
            userId, 30, "loyalty bonus", AdminId, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(account.PlanExpiresAt > expiryBeforeExtend);
        auditLogs.Verify(x => x.LogAsync(
            AdminId, AuditActions.PlanExtendedByAdmin, null,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtendSubscriptionAsync_WithoutAPriorPlan_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);

        var accounts = FakeAccounts(userId, account);
        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.ExtendSubscriptionAsync(
            userId, 30, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Conflict);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_MarksCancelled_KeepsPlanIdForHistory_AndAuditLogs()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var planId = Guid.NewGuid();
        account.ActivatePlanByAdmin(planId, DateTime.UtcNow.AddDays(30), Guid.NewGuid(), DateTime.UtcNow);

        var accounts = FakeAccounts(userId, account);
        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), auditLogs.Object);

        var result = await service.CancelSubscriptionAsync(
            userId, "requested by user", AdminId, "10.0.0.1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(planId, account.PlanId);
        auditLogs.Verify(x => x.LogAsync(
            AdminId, AuditActions.PlanCancelledByAdmin, "10.0.0.1",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivatePlanAsync_WhenSaveFails_RollsBackAndRethrows()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var plan = BuildPlan("premium");

        var accounts = FakeAccounts(userId, account);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByTierAsync("premium", It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var service = BuildService(accounts.Object, plans.Object, unitOfWork.Object, Mock.Of<IAuditLogService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivatePlanAsync(userId, "premium", 30, null, AdminId, null, CancellationToken.None));

        unitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3651)]
    public async Task ExtendSubscriptionAsync_WithOutOfRangeDuration_ReturnsBadRequest(int days)
    {
        var userId = Guid.NewGuid();
        var service = BuildService(
            Mock.Of<IUserAccountRepository>(), Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.ExtendSubscriptionAsync(userId, days, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExtendSubscriptionAsync_WhenUserAccountMissing_ReturnsUserNotFound()
    {
        var userId = Guid.NewGuid();
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.ExtendSubscriptionAsync(userId, 30, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task ExtendSubscriptionAsync_WhenSaveFails_RollsBackAndRethrows()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        account.ActivatePlanByAdmin(Guid.NewGuid(), DateTime.UtcNow.AddDays(10), Guid.NewGuid(), DateTime.UtcNow);

        var accounts = FakeAccounts(userId, account);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), unitOfWork.Object, Mock.Of<IAuditLogService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtendSubscriptionAsync(userId, 30, null, AdminId, null, CancellationToken.None));

        unitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_WhenUserAccountMissing_ReturnsUserNotFound()
    {
        var userId = Guid.NewGuid();
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.CancelSubscriptionAsync(userId, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_WithoutAPriorPlan_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);

        var accounts = FakeAccounts(userId, account);
        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.CancelSubscriptionAsync(userId, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Conflict);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_WhenSaveFails_RollsBackAndRethrows()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        account.ActivatePlanByAdmin(Guid.NewGuid(), DateTime.UtcNow.AddDays(10), Guid.NewGuid(), DateTime.UtcNow);

        var accounts = FakeAccounts(userId, account);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), unitOfWork.Object, Mock.Of<IAuditLogService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelSubscriptionAsync(userId, null, AdminId, null, CancellationToken.None));

        unitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivatePlanAsync_WhenUserAccountMissing_ReturnsUserNotFound()
    {
        var userId = Guid.NewGuid();
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        var service = BuildService(accounts.Object, Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>());

        var result = await service.ActivatePlanAsync(
            userId, "premium", 30, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.NotFound);
    }

    private static AdminSubscriptionService BuildService(
        IUserAccountRepository accounts,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs)
        => new(accounts, plans, unitOfWork, auditLogs, NullLogger<AdminSubscriptionService>.Instance);

    private static Mock<IUserAccountRepository> FakeAccounts(Guid userId, UserAccount account)
    {
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        return accounts;
    }

    private static Plan BuildPlan(string tier) => Plan.Create(
        tier: tier,
        nameKey: "PRICING.PREMIUM.NAME",
        taglineKey: "PRICING.PREMIUM.TAGLINE",
        priceKind: "monthly",
        priceUsd: 99m,
        featureKeys: new[] { "PRICING.PREMIUM.F1" },
        ctaKey: "PRICING.PREMIUM.CTA",
        isRecommended: true,
        displayOrder: 3,
        listingLimit: 250);
}
