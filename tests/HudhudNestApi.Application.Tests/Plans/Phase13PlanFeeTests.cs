using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Admin.Services;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Plans.Services;
using HudhudNestApi.Application.Users.Commands.SelectPlan;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Domain.Plans.Enums;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Plans;

/// <summary>QA Phase 13: free-tier protection (D04), pending-request cleanup (D06), billing notices (D03), repeat cancel (D08), reason cap (D07).</summary>
public sealed class Phase13PlanFeeTests
{
    private static Plan BuildPlan(string tier) => Plan.Create(
        tier: tier,
        nameKey: "PRICING." + tier.ToUpperInvariant() + ".NAME",
        taglineKey: "PRICING." + tier.ToUpperInvariant() + ".TAGLINE",
        priceKind: tier == "free" ? "free" : "monthly",
        priceUsd: tier == "free" ? 0m : 49m,
        featureKeys: new[] { "F1" },
        ctaKey: "CTA",
        isRecommended: false,
        displayOrder: 1,
        listingLimit: tier == "free" ? 1 : 50);

    private sealed class Requests : IPlanRequestRepository
    {
        public List<PlanRequest> Rows { get; } = new();
        public Task<PlanRequest?> GetPendingForUserAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(r => r.UserId == userId && r.Status == PlanRequestStatus.Pending));
        public Task<PlanRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
        public Task AddAsync(PlanRequest request, CancellationToken ct = default) { Rows.Add(request); return Task.CompletedTask; }
        public Task ReloadAsync(PlanRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static SelectPlanCommandHandler BuildSelect(UserAccount account, Plan free, Plan? current, Requests requests)
    {
        var identity = new Mock<IUserIdentityReadService>();
        identity.Setup(x => x.FindByIdAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountSnapshot(account.Id, account.Id, "o@example.com", null, true, false, true, false));
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByTierAsync("free", It.IsAny<CancellationToken>())).ReturnsAsync(free);
        if (current is not null)
            plans.Setup(x => x.GetByIdAsync(current.Id, It.IsAny<CancellationToken>())).ReturnsAsync(current);
        var agencies = new Mock<IAgencyRepository>();
        return new SelectPlanCommandHandler(identity.Object, accounts.Object, plans.Object, requests, agencies.Object, Mock.Of<IUnitOfWork>());
    }

    [Fact]
    public async Task SelectFree_WhileAPaidPlanIsInForce_IsRefused_AndNothingChanges()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var basic = BuildPlan("basic");
        account.ActivatePlanByAdmin(basic.Id, DateTime.UtcNow.AddDays(30), Guid.NewGuid(), DateTime.UtcNow);

        var handler = BuildSelect(account, BuildPlan("free"), basic, new Requests());

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new SelectPlanCommand(userId, "free"), CancellationToken.None));

        Assert.Equal(SelectPlanCommandHandler.PaidPlanActiveCode, ex.Code);
        Assert.Equal(basic.Id, account.PlanId);
        Assert.NotNull(account.PlanExpiresAt);
    }

    [Fact]
    public async Task SelectFree_AfterTheAdministratorCancelledThePaidPlan_IsAllowed()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var basic = BuildPlan("basic");
        var free = BuildPlan("free");
        account.ActivatePlanByAdmin(basic.Id, DateTime.UtcNow.AddDays(30), Guid.NewGuid(), DateTime.UtcNow);
        account.CancelPlan(Guid.NewGuid(), DateTime.UtcNow);

        var handler = BuildSelect(account, free, basic, new Requests());
        var result = await handler.Handle(new SelectPlanCommand(userId, "free"), CancellationToken.None);

        Assert.Equal(SelectPlanOutcome.Activated, result.Outcome);
        Assert.Equal(free.Id, account.PlanId);
    }

    [Fact]
    public async Task SelectFree_ClosesTheUsersPendingPaidRequest()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var premium = BuildPlan("premium");
        var requests = new Requests();
        var pending = PlanRequest.Create(account.Id, premium.Id, DateTime.UtcNow);
        requests.Rows.Add(pending);

        var handler = BuildSelect(account, BuildPlan("free"), null, requests);
        await handler.Handle(new SelectPlanCommand(userId, "free"), CancellationToken.None);

        Assert.Equal(PlanRequestStatus.Superseded, pending.Status);
    }

    private static (AdminSubscriptionService Service, Mock<INotificationService> Notes, UserAccount Account, Plan Plan, Requests Requests)
        BuildAdmin(Plan plan)
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", DateTime.UtcNow);
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByTierAsync(plan.Tier, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        plans.Setup(x => x.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var requests = new Requests();
        var notes = new Mock<INotificationService>();
        var service = new AdminSubscriptionService(
            accounts.Object, plans.Object, requests, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>(),
            new BillingNotifier(notes.Object, NullLogger<BillingNotifier>.Instance),
            NullLogger<AdminSubscriptionService>.Instance);
        return (service, notes, account, plan, requests);
    }

    [Fact]
    public async Task AdminActivate_SupersedesTheUsersPendingRequest()
    {
        var (service, _, account, plan, requests) = BuildAdmin(BuildPlan("basic"));
        var pending = PlanRequest.Create(account.Id, BuildPlan("premium").Id, DateTime.UtcNow);
        requests.Rows.Add(pending);

        var result = await service.ActivatePlanAsync(account.Id, "basic", 30, null, Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(PlanRequestStatus.Superseded, pending.Status);
    }

    [Fact]
    public async Task AdminCancel_Twice_SecondAnswersConflict()
    {
        var (service, _, account, _, _) = BuildAdmin(BuildPlan("basic"));
        await service.ActivatePlanAsync(account.Id, "basic", 30, null, Guid.NewGuid(), null, CancellationToken.None);

        var first = await service.CancelSubscriptionAsync(account.Id, null, Guid.NewGuid(), null, CancellationToken.None);
        var second = await service.CancelSubscriptionAsync(account.Id, null, Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Conflict);
    }

    [Fact]
    public async Task ApprovingAPlanRequest_NotifiesTheUser_WithThePlanAndTheEndDate()
    {
        var (service, notes, account, plan, requests) = BuildAdmin(BuildPlan("basic"));
        var request = PlanRequest.Create(account.Id, plan.Id, DateTime.UtcNow);
        requests.Rows.Add(request);

        var result = await service.ApprovePlanRequestAsync(request.Id, 30, null, Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        notes.Verify(x => x.NotifyBillingAsync(
            account.Id, NotificationType.PlanRequestApproved, It.IsAny<NotificationText>(),
            request.Id, $"plan-approved:{request.Id:N}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectingAPlanRequest_NotifiesTheUser_AndCarriesTheReason()
    {
        var (service, notes, account, plan, requests) = BuildAdmin(BuildPlan("basic"));
        var request = PlanRequest.Create(account.Id, plan.Id, DateTime.UtcNow);
        requests.Rows.Add(request);
        NotificationText? sent = null;
        notes.Setup(x => x.NotifyBillingAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<NotificationText>(),
                It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NotificationType, NotificationText, Guid?, string?, CancellationToken>((_, _, t, _, _, _) => sent = t)
            .Returns(Task.CompletedTask);

        await service.RejectPlanRequestAsync(request.Id, "الحوالة لم تصل", Guid.NewGuid(), null, CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal(NotificationKeys.PlanRequestRejectedWithReason, sent!.Key);
        notes.Verify(x => x.NotifyBillingAsync(
            account.Id, NotificationType.PlanRequestRejected, It.IsAny<NotificationText>(),
            request.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BillingNotifier_NeverThrows_WhenTheNotificationServiceFails()
    {
        var notes = new Mock<INotificationService>();
        notes.Setup(x => x.NotifyBillingAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<NotificationText>(),
                It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("push down"));
        var notifier = new BillingNotifier(notes.Object, NullLogger<BillingNotifier>.Instance);

        await notifier.PlanExpiredAsync(Guid.NewGuid(), "basic", DateTime.UtcNow, CancellationToken.None);
        await notifier.ListingFeeRejectedAsync(Guid.NewGuid(), Guid.NewGuid(), true, "عنوان", null, CancellationToken.None);
    }

    [Fact]
    public async Task BillingNotifier_ExpiryNotices_UseOneDedupeKeyPerEndDate()
    {
        var userId = Guid.NewGuid();
        var end = new DateTime(2026, 12, 1, 8, 30, 0, DateTimeKind.Utc);
        var keys = new List<string?>();
        var notes = new Mock<INotificationService>();
        notes.Setup(x => x.NotifyBillingAsync(
                It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<NotificationText>(),
                It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, NotificationType, NotificationText, Guid?, string?, CancellationToken>((_, _, _, _, k, _) => keys.Add(k))
            .Returns(Task.CompletedTask);
        var notifier = new BillingNotifier(notes.Object, NullLogger<BillingNotifier>.Instance);

        await notifier.PlanExpiringSoonAsync(userId, "basic", end, CancellationToken.None);
        await notifier.PlanExpiringSoonAsync(userId, "basic", end, CancellationToken.None);
        await notifier.PlanExpiringSoonAsync(userId, "basic", end.AddDays(30), CancellationToken.None);

        Assert.Equal(keys[0], keys[1]);
        Assert.NotEqual(keys[0], keys[2]);
    }
}
