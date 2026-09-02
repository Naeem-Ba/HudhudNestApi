using PropertyApi.Domain.Users;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Users.Enums;

namespace PropertyApi.Application.Tests.Users;

/// <summary>
/// Domain-level unit tests for UserAccount's admin-granted subscription lifecycle
/// (ActivatePlanByAdmin/ExtendPlan/CancelPlan/GetEffectivePlanStatus) — no mocks needed,
/// these are pure entity behavior. Mirrors SelectPlanCommandHandlerTests' sibling
/// self-service coverage.
/// </summary>
public sealed class UserAccountSubscriptionTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ActivatePlanByAdmin_SetsExpiryStatusAndSource()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        var planId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var expiresAt = Now.AddDays(30);

        account.ActivatePlanByAdmin(planId, expiresAt, adminId, Now);

        Assert.Equal(planId, account.PlanId);
        Assert.Equal(expiresAt, account.PlanExpiresAt);
        Assert.Equal(PlanStatus.Active, account.PlanStatus);
        Assert.Equal(PlanActivationSource.AdminGrant, account.PlanActivationSource);
        Assert.Equal(adminId, account.PlanGrantedByUserId);
        Assert.Null(account.PlanCancelledAt);
        Assert.Equal(EffectivePlanStatus.Active, account.GetEffectivePlanStatus(Now));
    }

    [Fact]
    public void ActivatePlanByAdmin_WithPastExpiry_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        Assert.Throws<ArgumentException>(() =>
            account.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(-1), Guid.NewGuid(), Now));
    }

    [Fact]
    public void ActivatePlanByAdmin_WithEmptyPlanId_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        Assert.Throws<ArgumentException>(() =>
            account.ActivatePlanByAdmin(Guid.Empty, Now.AddDays(30), Guid.NewGuid(), Now));
    }

    [Fact]
    public void ActivatePlanByAdmin_WithEmptyAdminId_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        Assert.Throws<ArgumentException>(() =>
            account.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(30), Guid.Empty, Now));
    }

    [Fact]
    public void ExtendPlan_WhenActiveAndFuture_AddsOnTopOfCurrentExpiry()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        var planId = Guid.NewGuid();
        var currentExpiry = Now.AddDays(10);
        account.ActivatePlanByAdmin(planId, currentExpiry, Guid.NewGuid(), Now);

        var adminId = Guid.NewGuid();
        account.ExtendPlan(TimeSpan.FromDays(30), adminId, Now);

        Assert.Equal(currentExpiry.AddDays(30), account.PlanExpiresAt);
        Assert.Equal(PlanStatus.Active, account.PlanStatus);
        Assert.Equal(adminId, account.PlanGrantedByUserId);
    }

    [Fact]
    public void ExtendPlan_WhenExpired_StartsFromNow()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        var planId = Guid.NewGuid();
        var pastExpiry = Now.AddDays(-5);
        account.ActivatePlanByAdmin(planId, Now.AddDays(1), Guid.NewGuid(), Now);
        // Simulate time passing beyond expiry by extending "now" forward past PlanExpiresAt.
        var laterNow = Now.AddDays(10);

        account.ExtendPlan(TimeSpan.FromDays(30), Guid.NewGuid(), laterNow);

        Assert.Equal(laterNow.AddDays(30), account.PlanExpiresAt);
        Assert.Equal(PlanStatus.Active, account.PlanStatus);
    }

    [Fact]
    public void ExtendPlan_WhenCancelled_StartsFromNow_AndReactivates()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(30), Guid.NewGuid(), Now);
        account.CancelPlan(Guid.NewGuid(), Now.AddDays(1));

        var laterNow = Now.AddDays(2);
        account.ExtendPlan(TimeSpan.FromDays(10), Guid.NewGuid(), laterNow);

        Assert.Equal(laterNow.AddDays(10), account.PlanExpiresAt);
        Assert.Equal(PlanStatus.Active, account.PlanStatus);
        Assert.Null(account.PlanCancelledAt);
    }

    [Fact]
    public void ExtendPlan_WithoutAPriorPlan_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        Assert.Throws<InvalidOperationException>(() =>
            account.ExtendPlan(TimeSpan.FromDays(30), Guid.NewGuid(), Now));
    }

    [Fact]
    public void ExtendPlan_WithZeroOrNegativePeriod_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(30), Guid.NewGuid(), Now);

        Assert.Throws<ArgumentException>(() =>
            account.ExtendPlan(TimeSpan.Zero, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() =>
            account.ExtendPlan(TimeSpan.FromDays(-1), Guid.NewGuid(), Now));
    }

    [Fact]
    public void ExtendPlan_WithEmptyAdminId_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(30), Guid.NewGuid(), Now);

        Assert.Throws<ArgumentException>(() =>
            account.ExtendPlan(TimeSpan.FromDays(10), Guid.Empty, Now));
    }

    [Fact]
    public void CancelPlan_WithEmptyAdminId_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(30), Guid.NewGuid(), Now);

        Assert.Throws<ArgumentException>(() =>
            account.CancelPlan(Guid.Empty, Now));
    }

    [Fact]
    public void CancelPlan_SetsCancelledStatus_ButKeepsPlanIdAndExpiryForHistory()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        var planId = Guid.NewGuid();
        var expiresAt = Now.AddDays(30);
        account.ActivatePlanByAdmin(planId, expiresAt, Guid.NewGuid(), Now);

        var adminId = Guid.NewGuid();
        account.CancelPlan(adminId, Now.AddDays(1));

        Assert.Equal(PlanStatus.Cancelled, account.PlanStatus);
        Assert.Equal(Now.AddDays(1), account.PlanCancelledAt);
        Assert.Equal(planId, account.PlanId); // history preserved on the row
        Assert.Equal(expiresAt, account.PlanExpiresAt); // history preserved on the row
        Assert.Equal(adminId, account.PlanGrantedByUserId);
        Assert.Equal(EffectivePlanStatus.Cancelled, account.GetEffectivePlanStatus(Now.AddDays(1)));
    }

    [Fact]
    public void CancelPlan_WithoutAPriorPlan_Throws()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        Assert.Throws<InvalidOperationException>(() =>
            account.CancelPlan(Guid.NewGuid(), Now));
    }

    [Fact]
    public void GetEffectivePlanStatus_NoPlanSelected_ReturnsNoPlan()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);

        Assert.Equal(EffectivePlanStatus.NoPlan, account.GetEffectivePlanStatus(Now));
        Assert.False(account.HasActivePlanBenefits(Now));
    }

    [Fact]
    public void GetEffectivePlanStatus_SelfServiceSelection_HasNoExpiryAndIsAlwaysActive()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.SelectPlan(Guid.NewGuid(), Now);

        Assert.Null(account.PlanExpiresAt);
        Assert.Equal(PlanActivationSource.SelfService, account.PlanActivationSource);
        Assert.Equal(EffectivePlanStatus.Active, account.GetEffectivePlanStatus(Now.AddYears(5)));
        Assert.True(account.HasActivePlanBenefits(Now.AddYears(5)));
    }

    [Fact]
    public void GetEffectivePlanStatus_PastExpiry_ReturnsExpired_WithoutAnyStatusMutation()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(1), Guid.NewGuid(), Now);

        var afterExpiry = Now.AddDays(2);

        Assert.Equal(EffectivePlanStatus.Expired, account.GetEffectivePlanStatus(afterExpiry));
        Assert.False(account.HasActivePlanBenefits(afterExpiry));
        // The stored flag itself never flips — Expired is purely computed (see
        // EffectivePlanStatus's doc comment), no background sweep required.
        Assert.Equal(PlanStatus.Active, account.PlanStatus);
    }

    [Fact]
    public void SelectPlan_AfterAnAdminGrant_ResetsExpiryStatusAndSource()
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Naeem", "User", Now);
        account.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(30), Guid.NewGuid(), Now);
        account.CancelPlan(Guid.NewGuid(), Now);

        var newPlanId = Guid.NewGuid();
        account.SelectPlan(newPlanId, Now.AddDays(1));

        Assert.Equal(newPlanId, account.PlanId);
        Assert.Null(account.PlanExpiresAt);
        Assert.Equal(PlanStatus.Active, account.PlanStatus);
        Assert.Equal(PlanActivationSource.SelfService, account.PlanActivationSource);
        Assert.Null(account.PlanCancelledAt);
    }

    [Fact]
    public void SubscriptionLifecyclePolicy_ExposesTheDocumentedDurationPresets()
    {
        Assert.Equal(new[] { 30, 90, 180, 365 }, SubscriptionLifecyclePolicy.SuggestedDurationDaysPresets);
        Assert.Equal(1, SubscriptionLifecyclePolicy.MinAdminDurationDays);
        Assert.Equal(3650, SubscriptionLifecyclePolicy.MaxAdminDurationDays);
    }
}
