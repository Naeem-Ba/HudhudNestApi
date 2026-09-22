using HudhudNestApi.Domain.Plans.Entities;

namespace HudhudNestApi.Application.Tests.Plans;

/// <summary>
/// Covers Plan.ListingLimit's invariant — the source of truth for listing quota
/// (BACKEND-ISSUES.md §B-3) must never be constructible with a value that would make
/// IListingQuotaPolicy behave nonsensically (a limit of zero or negative).
/// </summary>
public sealed class PlanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Create_WithNonPositiveListingLimit_Throws(int listingLimit)
    {
        Assert.Throws<ArgumentException>(() => BuildPlan(listingLimit));
    }

    [Fact]
    public void Create_WithNullListingLimit_Succeeds_AndMeansUnlimited()
    {
        var plan = BuildPlan(null);

        Assert.Null(plan.ListingLimit);
    }

    [Fact]
    public void Create_WithPositiveListingLimit_Succeeds()
    {
        var plan = BuildPlan(250);

        Assert.Equal(250, plan.ListingLimit);
    }

    private static Plan BuildPlan(int? listingLimit) => Plan.Create(
        tier: "premium",
        nameKey: "PRICING.PREMIUM.NAME",
        taglineKey: "PRICING.PREMIUM.TAGLINE",
        priceKind: "monthly",
        priceUsd: 99m,
        featureKeys: new[] { "PRICING.PREMIUM.F1" },
        ctaKey: "PRICING.PREMIUM.CTA",
        isRecommended: true,
        displayOrder: 3,
        listingLimit: listingLimit);
}
