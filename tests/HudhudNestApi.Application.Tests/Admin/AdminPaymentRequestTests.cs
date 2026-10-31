using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Plans.Services;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Admin.Services;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Domain.Plans.Enums;
using HudhudNestApi.Domain.Transactions.Entities;
using HudhudNestApi.Domain.Transactions.Enums;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Admin;

/// <summary>
/// Plans and featured placement are requested by the user and activated only by an
/// administrator who confirmed the payment. These pin the administrator's half: approval
/// is what grants the plan, a request resolves once, and a rejection changes nothing.
/// </summary>
public sealed class AdminPaymentRequestTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    // ── Plan requests ────────────────────────────────────────────

    [Fact]
    public async Task ApprovePlanRequest_ActivatesThePlan_ResolvesTheRequest_AndAuditLogs()
    {
        var (account, plan, request) = Pending("premium");
        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildSubscriptions(account, plan, request, auditLogs);

        var result = await service.ApprovePlanRequestAsync(
            request.Id, 30, "bank transfer ref 123", AdminId, "127.0.0.1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(plan.Id, account.PlanId);
        Assert.NotNull(account.PlanExpiresAt);
        Assert.Equal(PlanRequestStatus.Approved, request.Status);
        Assert.Equal(AdminId, request.ResolvedByUserId);
        auditLogs.Verify(x => x.LogAsync(
            AdminId,
            AuditActions.PlanRequestApprovedByAdmin,
            "127.0.0.1",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApprovePlanRequest_Twice_GrantsOnce_AndReportsConflict()
    {
        var (account, plan, request) = Pending("premium");
        var service = BuildSubscriptions(account, plan, request, new Mock<IAuditLogService>());

        await service.ApprovePlanRequestAsync(request.Id, 30, null, AdminId, null, CancellationToken.None);
        var firstExpiry = account.PlanExpiresAt;

        var second = await service.ApprovePlanRequestAsync(request.Id, 365, null, AdminId, null, CancellationToken.None);

        Assert.False(second.Succeeded);
        Assert.True(second.Conflict);
        Assert.Equal(firstExpiry, account.PlanExpiresAt);
    }

    [Fact]
    public async Task ApprovePlanRequest_WithAnOutOfRangeDuration_ChangesNothing()
    {
        var (account, plan, request) = Pending("premium");
        var service = BuildSubscriptions(account, plan, request, new Mock<IAuditLogService>());

        var result = await service.ApprovePlanRequestAsync(request.Id, 0, null, AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(account.PlanId);
        Assert.Equal(PlanRequestStatus.Pending, request.Status);
    }

    [Fact]
    public async Task ApprovePlanRequest_ForAnUnknownRequest_IsNotFound()
    {
        var (account, plan, request) = Pending("premium");
        var service = BuildSubscriptions(account, plan, request, new Mock<IAuditLogService>());

        var result = await service.ApprovePlanRequestAsync(
            Guid.NewGuid(), 30, null, AdminId, null, CancellationToken.None);

        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task RejectPlanRequest_ChangesNothingOnTheAccount_AndAuditLogs()
    {
        var (account, plan, request) = Pending("premium");
        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildSubscriptions(account, plan, request, auditLogs);

        var result = await service.RejectPlanRequestAsync(
            request.Id, "payment not received", AdminId, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(account.PlanId);
        Assert.Equal(PlanRequestStatus.Rejected, request.Status);
        Assert.Equal("payment not received", request.ResolutionNote);
        auditLogs.Verify(x => x.LogAsync(
            AdminId,
            AuditActions.PlanRequestRejectedByAdmin,
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectPlanRequest_AfterItWasApproved_IsAConflict_AndKeepsThePlan()
    {
        var (account, plan, request) = Pending("premium");
        var service = BuildSubscriptions(account, plan, request, new Mock<IAuditLogService>());

        await service.ApprovePlanRequestAsync(request.Id, 30, null, AdminId, null, CancellationToken.None);
        var result = await service.RejectPlanRequestAsync(request.Id, null, AdminId, null, CancellationToken.None);

        Assert.True(result.Conflict);
        Assert.Equal(plan.Id, account.PlanId);
        Assert.Equal(PlanRequestStatus.Approved, request.Status);
    }

    // ── Featured requests ────────────────────────────────────────

    [Fact]
    public async Task RejectFeatured_MarksThePendingFeeFailed_AndAuditLogs()
    {
        var fee = FeaturedFee();
        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildPayments(fee, auditLogs);

        var result = await service.RejectFeaturedAsync(fee.Id, "not paid", AdminId, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(TransactionStatus.Failed, fee.Status);
        auditLogs.Verify(x => x.LogAsync(
            AdminId,
            AuditActions.FeaturedRequestRejectedByAdmin,
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectFeatured_AfterPaymentWasConfirmed_IsAConflict_AndKeepsTheFeeCompleted()
    {
        var fee = FeaturedFee();
        fee.MarkCompleted();
        var service = BuildPayments(fee, new Mock<IAuditLogService>());

        var result = await service.RejectFeaturedAsync(fee.Id, null, AdminId, null, CancellationToken.None);

        Assert.True(result.Conflict);
        Assert.Equal(TransactionStatus.Completed, fee.Status);
    }

    [Fact]
    public async Task RejectFeatured_ForAnExtensionFee_IsAConflict()
    {
        var fee = Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, TransactionType.ListingExtensionFee,
            1m, 1, 1m, TransactionPaymentMethod.BankTransfer);
        var service = BuildPayments(fee, new Mock<IAuditLogService>());

        var result = await service.RejectFeaturedAsync(fee.Id, null, AdminId, null, CancellationToken.None);

        Assert.True(result.Conflict);
        Assert.Equal(TransactionStatus.Pending, fee.Status);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static (UserAccount Account, Plan Plan, PlanRequest Request) Pending(string tier)
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", DateTime.UtcNow);
        var plan = Plan.Create(
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
        var request = PlanRequest.Create(account.Id, plan.Id, DateTime.UtcNow);
        return (account, plan, request);
    }

    private static Transaction FeaturedFee() => Transaction.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, TransactionType.FeaturedListingFee,
        5m, 1, 1m, TransactionPaymentMethod.BankTransfer);

    private static AdminSubscriptionService BuildSubscriptions(
        UserAccount account,
        Plan plan,
        PlanRequest request,
        Mock<IAuditLogService> auditLogs)
    {
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var requests = new Mock<IPlanRequestRepository>();
        requests.Setup(x => x.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);

        return new AdminSubscriptionService(
            accounts.Object,
            plans.Object,
            requests.Object,
            Mock.Of<IUnitOfWork>(),
            auditLogs.Object,
            new BillingNotifier(Mock.Of<INotificationService>(), NullLogger<BillingNotifier>.Instance),
            NullLogger<AdminSubscriptionService>.Instance);
    }

    private static AdminPaymentRequestService BuildPayments(Transaction fee, Mock<IAuditLogService> auditLogs)
    {
        var fees = new Mock<IListingFeeRepository>();
        fees.Setup(x => x.GetByIdAsync(fee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(fee);

        return new AdminPaymentRequestService(
            Mock.Of<IAdminPaymentRequestQueryRepository>(),
            fees.Object,
            Mock.Of<IPropertyRepository>(),
            Mock.Of<IUnitOfWork>(),
            auditLogs.Object,
            new BillingNotifier(Mock.Of<INotificationService>(), NullLogger<BillingNotifier>.Instance),
            NullLogger<AdminPaymentRequestService>.Instance);
    }
}
