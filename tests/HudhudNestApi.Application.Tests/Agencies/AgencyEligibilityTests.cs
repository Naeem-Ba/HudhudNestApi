using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Services;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Agencies;

/// <summary>
/// The two agency preconditions, each asserted on its own: a verified account (email OR
/// phone confirmed) and a paid plan in force. Both answer with a stable code, because the
/// client has to turn each into a different Arabic/English/German message and a different
/// place to send the user next.
/// </summary>
public sealed class AgencyEligibilityTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EnsureVerifiedAccount_WithEmailOrPhoneConfirmed_Passes(
        bool emailConfirmed,
        bool phoneConfirmed)
    {
        var userId = Guid.NewGuid();
        var policy = BuildPolicy(
            Snapshot(userId, emailConfirmed, phoneConfirmed),
            account: null,
            plan: null);

        await policy.EnsureVerifiedAccountAsync(userId, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureVerifiedAccount_WithNeitherConfirmed_ThrowsForbiddenWithCode()
    {
        var userId = Guid.NewGuid();
        var policy = BuildPolicy(Snapshot(userId, false, false), account: null, plan: null);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => policy.EnsureVerifiedAccountAsync(userId, CancellationToken.None));

        Assert.Equal(AgencyEligibilityPolicy.AccountNotVerifiedCode, ex.Code);
    }

    [Fact]
    public async Task EnsurePaidPlan_WithNoPlanSelected_ThrowsForbiddenWithCode()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);

        var policy = BuildPolicy(Snapshot(userId, true, false), account, plan: null);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => policy.EnsurePaidPlanAsync(userId, CancellationToken.None));

        Assert.Equal(AgencyEligibilityPolicy.PaidPlanRequiredCode, ex.Code);
    }

    [Fact]
    public async Task EnsurePaidPlan_OnTheFreeTier_ThrowsForbiddenWithCode()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var free = BuildPlan("free");
        account.SelectPlan(free.Id, DateTime.UtcNow);

        var policy = BuildPolicy(Snapshot(userId, true, false), account, free);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => policy.EnsurePaidPlanAsync(userId, CancellationToken.None));

        Assert.Equal(AgencyEligibilityPolicy.PaidPlanRequiredCode, ex.Code);
    }

    [Fact]
    public async Task EnsurePaidPlan_WithACancelledPaidPlan_ThrowsForbiddenWithCode()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var premium = BuildPlan("premium");
        account.SelectPlan(premium.Id, DateTime.UtcNow);
        account.CancelPlan(userId, DateTime.UtcNow);

        var policy = BuildPolicy(Snapshot(userId, true, false), account, premium);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => policy.EnsurePaidPlanAsync(userId, CancellationToken.None));

        Assert.Equal(AgencyEligibilityPolicy.PaidPlanRequiredCode, ex.Code);
    }

    [Fact]
    public async Task EnsurePaidPlan_WithAnActivePaidPlan_Passes()
    {
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Naeem", "User", DateTime.UtcNow);
        var premium = BuildPlan("premium");
        account.SelectPlan(premium.Id, DateTime.UtcNow);

        var policy = BuildPolicy(Snapshot(userId, true, false), account, premium);

        await policy.EnsurePaidPlanAsync(userId, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static IdentityAccountSnapshot Snapshot(
        Guid userId,
        bool emailConfirmed,
        bool phoneConfirmed)
        => new(
            IdentityId: userId,
            UserAccountId: userId,
            Email: "owner@example.com",
            PhoneNumber: "+963911234567",
            EmailConfirmed: emailConfirmed,
            PhoneConfirmed: phoneConfirmed,
            HasPassword: true,
            IsDeleted: false);

    private static Plan BuildPlan(string tier) => Plan.Create(
        tier: tier,
        nameKey: $"PRICING.{tier.ToUpperInvariant()}.NAME",
        taglineKey: $"PRICING.{tier.ToUpperInvariant()}.TAGLINE",
        priceKind: tier == "free" ? "free" : "monthly",
        priceUsd: tier == "free" ? 0m : 99m,
        featureKeys: [$"PRICING.{tier.ToUpperInvariant()}.F1"],
        ctaKey: $"PRICING.{tier.ToUpperInvariant()}.CTA",
        isRecommended: false,
        displayOrder: 1,
        listingLimit: 10);

    private static IAgencyEligibilityPolicy BuildPolicy(
        IdentityAccountSnapshot? snapshot,
        UserAccount? account,
        Plan? plan)
    {
        var identity = new Mock<IUserIdentityReadService>();
        identity
            .Setup(x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var accounts = new Mock<IUserAccountRepository>();
        accounts
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var plans = new Mock<IPlanRepository>();
        plans
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        return new AgencyEligibilityPolicy(identity.Object, accounts.Object, plans.Object);
    }
}
