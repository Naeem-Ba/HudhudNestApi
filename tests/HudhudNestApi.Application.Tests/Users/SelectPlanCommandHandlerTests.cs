using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Commands.SelectPlan;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Domain.Plans.Enums;
using HudhudNestApi.Domain.Agencies.Entities;
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
            identity.Object, accounts.Object, plans.Object, new FakePlanRequests(), NoOwnedAgency().Object, unitOfWork.Object);

        var result = await handler.Handle(
            new SelectPlanCommand(userId, "free"), CancellationToken.None);

        Assert.Equal(SelectPlanOutcome.Activated, result.Outcome);
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
            identity.Object, accounts.Object, plans.Object, new FakePlanRequests(), NoOwnedAgency().Object, Mock.Of<IUnitOfWork>());

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
            new FakePlanRequests(),
            NoOwnedAgency().Object,
            Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(
            new SelectPlanCommand(userId, "free"), CancellationToken.None);

        Assert.Equal(SelectPlanOutcome.AccountNotFound, result.Outcome);
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
            identity.Object,
            accounts.Object,
            Mock.Of<IPlanRepository>(),
            new FakePlanRequests(),
            NoOwnedAgency().Object,
            Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(
            new SelectPlanCommand(userId, "free"), CancellationToken.None);

        Assert.Equal(SelectPlanOutcome.AccountNotFound, result.Outcome);
    }

    [Fact]
    public async Task Handle_FreeTier_WhenCallerOwnsAnAgency_ThrowsForbiddenWithCode()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var plan = BuildPlan("free");

        var identity = BuildConfirmedIdentity(userId);
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByTierAsync("free", It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetByOwnerAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Agency.Create("مكتب الشام", "sham-realty", userId, "SY", DateTime.UtcNow));

        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new SelectPlanCommandHandler(
            identity.Object, accounts.Object, plans.Object, new FakePlanRequests(), agencies.Object, unitOfWork.Object);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new SelectPlanCommand(userId, "free"), CancellationToken.None));

        Assert.Equal(SelectPlanCommandHandler.AgencyFreePlanNotAllowedCode, ex.Code);
        Assert.Null(account.PlanId);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PaidTier_RecordsAPendingRequest_AndGrantsNothing()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var plan = BuildPlan("premium");
        var requests = new FakePlanRequests();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = BuildHandler(account, plan, requests, unitOfWork);

        var result = await handler.Handle(
            new SelectPlanCommand(userId, "premium"), CancellationToken.None);

        Assert.Equal(SelectPlanOutcome.PendingAdminApproval, result.Outcome);
        Assert.Null(account.PlanId);
        var pending = Assert.Single(requests.Rows);
        Assert.Equal(PlanRequestStatus.Pending, pending.Status);
        Assert.Equal(plan.Id, pending.PlanId);
        Assert.Equal(pending.Id, result.RequestId);
    }

    [Fact]
    public async Task Handle_PaidTier_DoesNotChangeAPlanTheAccountAlreadyHas()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var free = BuildPlan("free");
        account.SelectPlan(free.Id, DateTime.UtcNow);

        var handler = BuildHandler(account, BuildPlan("premium"), new FakePlanRequests(), new Mock<IUnitOfWork>());

        await handler.Handle(new SelectPlanCommand(userId, "premium"), CancellationToken.None);

        Assert.Equal(free.Id, account.PlanId);
    }

    [Fact]
    public async Task Handle_PaidTier_AskedTwiceForTheSamePlan_KeepsOneRequest()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var requests = new FakePlanRequests();
        var handler = BuildHandler(account, BuildPlan("premium"), requests, new Mock<IUnitOfWork>());

        var first = await handler.Handle(new SelectPlanCommand(userId, "premium"), CancellationToken.None);
        var second = await handler.Handle(new SelectPlanCommand(userId, "premium"), CancellationToken.None);

        Assert.Single(requests.Rows);
        Assert.Equal(first.RequestId, second.RequestId);
    }

    [Fact]
    public async Task Handle_PaidTier_AskedForADifferentPlan_SupersedesTheEarlierRequest()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var requests = new FakePlanRequests();
        var plans = new Mock<IPlanRepository>();
        var basic = BuildPlan("basic");
        var premium = BuildPlan("premium");
        plans.Setup(x => x.GetByTierAsync("basic", It.IsAny<CancellationToken>())).ReturnsAsync(basic);
        plans.Setup(x => x.GetByTierAsync("premium", It.IsAny<CancellationToken>())).ReturnsAsync(premium);
        var handler = BuildHandler(account, plans, requests, new Mock<IUnitOfWork>());

        await handler.Handle(new SelectPlanCommand(userId, "basic"), CancellationToken.None);
        await handler.Handle(new SelectPlanCommand(userId, "premium"), CancellationToken.None);

        Assert.Equal(2, requests.Rows.Count);
        Assert.Equal(PlanRequestStatus.Superseded, requests.Rows.Single(r => r.PlanId == basic.Id).Status);
        Assert.Equal(PlanRequestStatus.Pending, requests.Rows.Single(r => r.PlanId == premium.Id).Status);
    }

    [Fact]
    public async Task Handle_PaidTier_WhenCallerOwnsAnAgency_IsRecordedAsARequestToo()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var requests = new FakePlanRequests();

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetByOwnerAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Agency.Create("مكتب الشام", "sham-realty", userId, "SY", DateTime.UtcNow));

        var handler = BuildHandler(account, BuildPlan("premium"), requests, new Mock<IUnitOfWork>(), agencies);

        var result = await handler.Handle(
            new SelectPlanCommand(userId, "premium"), CancellationToken.None);

        Assert.Equal(SelectPlanOutcome.PendingAdminApproval, result.Outcome);
        Assert.Single(requests.Rows);
    }

    private static SelectPlanCommandHandler BuildHandler(
        UserAccount account,
        Plan plan,
        FakePlanRequests requests,
        Mock<IUnitOfWork> unitOfWork,
        Mock<IAgencyRepository>? agencies = null)
    {
        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByTierAsync(plan.Tier, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return BuildHandler(account, plans, requests, unitOfWork, agencies);
    }

    private static SelectPlanCommandHandler BuildHandler(
        UserAccount account,
        Mock<IPlanRepository> plans,
        FakePlanRequests requests,
        Mock<IUnitOfWork> unitOfWork,
        Mock<IAgencyRepository>? agencies = null)
    {
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        return new SelectPlanCommandHandler(
            BuildConfirmedIdentity(account.Id).Object,
            accounts.Object,
            plans.Object,
            requests,
            (agencies ?? NoOwnedAgency()).Object,
            unitOfWork.Object);
    }

    /// <summary>In-memory plan-request store: enough of the repository for the handler's rules.</summary>
    private sealed class FakePlanRequests : IPlanRequestRepository
    {
        public List<PlanRequest> Rows { get; } = new();

        public Task<PlanRequest?> GetPendingForUserAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(r => r.UserId == userId && r.Status == PlanRequestStatus.Pending));

        public Task<PlanRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));

        public Task AddAsync(PlanRequest request, CancellationToken ct = default)
        {
            Rows.Add(request);
            return Task.CompletedTask;
        }

        public Task ReloadAsync(PlanRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>A caller who owns no agency — the free tier stays open to them.</summary>
    private static Mock<IAgencyRepository> NoOwnedAgency()
    {
        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agency?)null);
        return agencies;
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
        priceKind: tier == "free" ? "free" : "monthly",
        priceUsd: tier == "free" ? 0m : 99m,
        featureKeys: new[] { "PRICING.FREE.F1" },
        ctaKey: "PRICING.FREE.CTA",
        isRecommended: false,
        displayOrder: 1,
        listingLimit: 1);
}
