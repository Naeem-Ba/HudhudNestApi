using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Plans.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Plans.Entities;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Listings;
using Xunit;

namespace PropertyApi.Integration.Tests.Listings;

/// <summary>
/// Exercises Infrastructure's real ListingQuotaPolicy directly (internal, exposed to this
/// assembly via InternalsVisibleTo) against mocked IPlanRepository/IAgencyRepository — no
/// database needed, since the class itself only calls those two repository seams. This is
/// where the agency-owner-vs-member-plan business rule from BACKEND-ISSUES.md §B-3 is
/// actually proven, since PropertyApi.Application.Tests' CreatePropertyCommandHandler tests
/// only prove the handler delegates to whatever IListingQuotaPolicy returns — not how the
/// real implementation resolves that number.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class ListingQuotaPolicyTests
{
    // ── Individual owner ─────────────────────────────────────────

    [Fact]
    public async Task IndividualOwner_OnFreePlan_ResolvesToOne()
    {
        var owner = BuildAccountWithPlan(out var freePlanId);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByIdAsync(freePlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildPlan("free", 1));

        var policy = new ListingQuotaPolicy(plans.Object, Mock.Of<IAgencyRepository>());

        var limit = await policy.GetActiveListingLimitAsync(owner);

        Assert.Equal(1, limit);
    }

    [Fact]
    public async Task IndividualOwner_OnElitePlan_ResolvesToUnlimited()
    {
        var owner = BuildAccountWithPlan(out var elitePlanId);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByIdAsync(elitePlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildPlan("elite", listingLimit: null));

        var policy = new ListingQuotaPolicy(plans.Object, Mock.Of<IAgencyRepository>());

        var limit = await policy.GetActiveListingLimitAsync(owner);

        Assert.Equal(int.MaxValue, limit);
    }

    [Fact]
    public async Task IndividualOwner_WithNoPlanSelected_ThrowsInsteadOfGrantingAnyQuota()
    {
        var owner = UserAccount.Create(Guid.NewGuid(), "Test", "Owner", DateTime.UtcNow);
        Assert.Null(owner.PlanId);

        var policy = new ListingQuotaPolicy(
            Mock.Of<IPlanRepository>(), Mock.Of<IAgencyRepository>());

        await Assert.ThrowsAsync<ConflictException>(
            () => policy.GetActiveListingLimitAsync(owner));
    }

    [Fact]
    public async Task IndividualOwner_WithPlanIdThatNoLongerResolves_ThrowsRatherThanBypassing()
    {
        // Should not happen in production (UserAccount.PlanId has a Restrict FK to Plans),
        // but a policy that returned "unlimited" here would turn a data anomaly into a
        // silent quota bypass — it must fail closed instead.
        var owner = BuildAccountWithPlan(out var planId);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Plan?)null);

        var policy = new ListingQuotaPolicy(plans.Object, Mock.Of<IAgencyRepository>());

        await Assert.ThrowsAsync<ConflictException>(
            () => policy.GetActiveListingLimitAsync(owner));
    }

    // ── Agency (owner's plan governs the pooled limit) ───────────

    [Fact]
    public async Task AgencyMember_ResolvesTheLimitFromTheAgencyOwnersPlan_NotTheMembersOwn()
    {
        var ownerId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var agencyOwnerAccount = BuildAccountWithPlan(out var ownerPlanId, ownerId);
        var member = BuildAccountInAgency(memberId, agencyId, out var memberPlanId);

        var agency = Agency.Create("Test Agency", "test-agency", ownerId, "SY", DateTime.UtcNow);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);
        agencies.Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agencyOwnerAccount);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(x => x.GetByIdAsync(ownerPlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildPlan("premium", 250));
        // The member's OWN plan resolves to a different number — it must never be consulted.
        plans.Setup(x => x.GetByIdAsync(memberPlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildPlan("free", 1));

        var policy = new ListingQuotaPolicy(plans.Object, agencies.Object);

        var limit = await policy.GetActiveListingLimitAsync(member);

        Assert.Equal(250, limit);
        plans.Verify(
            x => x.GetByIdAsync(memberPlanId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AgencyMember_CannotInflateThePool_BySelectingAHigherPersonalPlan()
    {
        // Same scenario as above, restated as the specific abuse case: a member on a richer
        // individual plan than the agency owner must still be capped by the OWNER's number.
        var ownerId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var agencyOwnerAccount = BuildAccountWithPlan(out var ownerPlanId, ownerId);
        var member = BuildAccountInAgency(memberId, agencyId, out var memberPlanId);

        var agency = Agency.Create("Test Agency", "test-agency-2", ownerId, "SY", DateTime.UtcNow);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);
        agencies.Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agencyOwnerAccount);

        var plans = new Mock<IPlanRepository>();
        // Owner is on Free (1); member picked Elite (unlimited) for themselves.
        plans.Setup(x => x.GetByIdAsync(ownerPlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildPlan("free", 1));
        plans.Setup(x => x.GetByIdAsync(memberPlanId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildPlan("elite", listingLimit: null));

        var policy = new ListingQuotaPolicy(plans.Object, agencies.Object);

        var limit = await policy.GetActiveListingLimitAsync(member);

        Assert.Equal(1, limit);
    }

    [Fact]
    public async Task AgencyMember_WhenTheAgencyOwnerHasNoPlanSelected_ThrowsRatherThanBypassing()
    {
        var ownerId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var ownerWithoutPlan = UserAccount.Create(ownerId, "Owner", "NoPlan", DateTime.UtcNow);
        var member = BuildAccountInAgency(memberId, agencyId, out _);

        var agency = Agency.Create("Test Agency", "test-agency-3", ownerId, "SY", DateTime.UtcNow);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);
        agencies.Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerWithoutPlan);

        var policy = new ListingQuotaPolicy(Mock.Of<IPlanRepository>(), agencies.Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => policy.GetActiveListingLimitAsync(member));
    }

    [Fact]
    public async Task AgencyMember_WhenTheAgencyCannotBeFound_ThrowsRatherThanBypassing()
    {
        var agencyId = Guid.NewGuid();
        var member = BuildAccountInAgency(Guid.NewGuid(), agencyId, out _);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agency?)null);

        var policy = new ListingQuotaPolicy(Mock.Of<IPlanRepository>(), agencies.Object);

        await Assert.ThrowsAsync<ConflictException>(
            () => policy.GetActiveListingLimitAsync(member));
    }

    // ── Fixtures ──────────────────────────────────────────────────

    private static UserAccount BuildAccountWithPlan(out Guid planId, Guid? userId = null)
    {
        var account = UserAccount.Create(
            userId ?? Guid.NewGuid(), "Test", "Owner", DateTime.UtcNow);
        planId = Guid.NewGuid();
        account.SelectPlan(planId, DateTime.UtcNow);
        return account;
    }

    private static UserAccount BuildAccountInAgency(Guid userId, Guid agencyId, out Guid planId)
    {
        var account = UserAccount.Create(userId, "Test", "Member", DateTime.UtcNow);
        account.JoinAgency(agencyId, DateTime.UtcNow);
        planId = Guid.NewGuid();
        account.SelectPlan(planId, DateTime.UtcNow);
        return account;
    }

    private static Plan BuildPlan(string tier, int? listingLimit) => Plan.Create(
        tier: tier,
        nameKey: $"PRICING.{tier.ToUpperInvariant()}.NAME",
        taglineKey: $"PRICING.{tier.ToUpperInvariant()}.TAGLINE",
        priceKind: tier == "free" ? "free" : tier == "elite" ? "contact" : "monthly",
        priceUsd: tier == "free" ? 0m : tier == "elite" ? null : 99m,
        featureKeys: new[] { $"PRICING.{tier.ToUpperInvariant()}.F1" },
        ctaKey: $"PRICING.{tier.ToUpperInvariant()}.CTA",
        isRecommended: false,
        displayOrder: 1,
        listingLimit: listingLimit);
}
