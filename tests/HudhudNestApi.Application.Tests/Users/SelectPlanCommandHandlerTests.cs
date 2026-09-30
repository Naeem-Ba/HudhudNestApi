using Moq;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Commands.SelectPlan;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Users;

public sealed class SelectPlanCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithKnownActiveTier_SelectsPlanAndSaves()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var plan = BuildPlan("free");

        var identity = BuildConfirmedIdentity(userId);
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByTierAsync("free", It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new SelectPlanCommandHandler(
            identity.Object, accounts.Object, plans.Object, unitOfWork.Object);

        var result = await handler.Handle(
            new SelectPlanCommand(userId, "free"), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(plan.Id, account.PlanId);
        Assert.NotNull(account.PlanSelectedAt);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithUnknownTier_ThrowsValidationException()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);

        var identity = BuildConfirmedIdentity(userId);
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var plans = new Mock<IPlanRepository>();
        plans
            .Setup(x => x.GetByTierAsync("not-a-real-tier", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Plan?)null);

        var handler = new SelectPlanCommandHandler(
            identity.Object, accounts.Object, plans.Object, Mock.Of<IUnitOfWork>());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SelectPlanCommand(userId, "not-a-real-tier"), CancellationToken.None));

        Assert.Equal("PLAN_TIER_UNKNOWN", ex.ErrorCodes[nameof(SelectPlanCommand.Tier)][0]);
        Assert.Null(account.PlanId);
    }

    [Fact]
    public async Task Handle_WhenIdentityIsMissing_ReturnsFalse()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IUserIdentityReadService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        var handler = new SelectPlanCommandHandler(
            identity.Object,
            Mock.Of<IUserAccountRepository>(),
            Mock.Of<IPlanRepository>(),
            Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(
            new SelectPlanCommand(userId, "free"), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Handle_WhenUserAccountIsMissing_ReturnsFalse()
    {
        var userId = Guid.NewGuid();

        var identity = BuildConfirmedIdentity(userId);
        var accounts = new Mock<IUserAccountRepository>();
        accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        var handler = new SelectPlanCommandHandler(
            identity.Object, accounts.Object, Mock.Of<IPlanRepository>(), Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(
            new SelectPlanCommand(userId, "free"), CancellationToken.None);

        Assert.False(result);
    }

    private static Mock<IUserIdentityReadService> BuildConfirmedIdentity(Guid userId)
    {
        var identity = new Mock<IUserIdentityReadService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountSnapshot(
                IdentityId: userId,
                UserAccountId: userId,
                Email: "owner@example.com",
                PhoneNumber: null,
                EmailConfirmed: true,
                PhoneConfirmed: false,
                HasPassword: true,
                IsDeleted: false));
        return identity;
    }

    private static Plan BuildPlan(string tier) => Plan.Create(
        tier: tier,
        nameKey: "PRICING.FREE.NAME",
        taglineKey: "PRICING.FREE.TAGLINE",
        priceKind: "free",
        priceUsd: 0m,
        featureKeys: new[] { "PRICING.FREE.F1" },
        ctaKey: "PRICING.FREE.CTA",
        isRecommended: false,
        displayOrder: 1,
        listingLimit: 1);
}
