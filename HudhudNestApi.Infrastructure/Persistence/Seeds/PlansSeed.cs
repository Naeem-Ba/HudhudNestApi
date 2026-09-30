using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Domain.Plans.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// Seeds the four tiers, mirroring the Wohnungsmieten frontend's
/// src/app/core/models/subscription-plan.model.ts SUBSCRIPTION_PLANS constant key
/// for key — the two must stay in sync, since neither owns display text (that's
/// entirely frontend i18n).
///
/// ListingLimit values mirror the same frontend's pricing table (FRONTEND_BACKEND_CONTRACT.md
/// §11.1): 1 for Free (also RELEASE-BLOCKERS-AR.md's former FreeTierActiveListingLimit
/// constant, now retired in favor of this being the one source), 50 for Basic, 250 for
/// Premium, and null (unlimited) for Elite's "negotiated" quota.
///
/// EnsureAsync below only inserts a tier that does not exist yet — it does not update
/// ListingLimit on a tier that was already seeded before this column existed. Environments
/// migrated from before this change get their existing rows backfilled by migration
/// 20260827091846_AddPlanListingLimit's data migration instead; this seed only matters for a
/// brand-new database.
/// </summary>
public static class PlansSeed
{
    public static async Task SeedAsync(AppDbContext context)
    {
        await EnsureAsync(context, Plan.Create(
            tier: "free",
            nameKey: "PRICING.FREE.NAME",
            taglineKey: "PRICING.FREE.TAGLINE",
            priceKind: "free",
            priceUsd: 0m,
            featureKeys: new[]
            {
                "PRICING.FREE.F1", "PRICING.FREE.F2", "PRICING.FREE.F3", "PRICING.FREE.F4"
            },
            ctaKey: "PRICING.FREE.CTA",
            isRecommended: false,
            displayOrder: 1,
            listingLimit: 1,
            noteKey: "PRICING.FREE.NOTE"));

        await EnsureAsync(context, Plan.Create(
            tier: "basic",
            nameKey: "PRICING.BASIC.NAME",
            taglineKey: "PRICING.BASIC.TAGLINE",
            priceKind: "monthly",
            priceUsd: 49m,
            featureKeys: new[]
            {
                "PRICING.BASIC.F1", "PRICING.BASIC.F2", "PRICING.BASIC.F3",
                "PRICING.BASIC.F4", "PRICING.BASIC.F5"
            },
            ctaKey: "PRICING.BASIC.CTA",
            isRecommended: false,
            displayOrder: 2,
            listingLimit: 50));

        await EnsureAsync(context, Plan.Create(
            tier: "premium",
            nameKey: "PRICING.PREMIUM.NAME",
            taglineKey: "PRICING.PREMIUM.TAGLINE",
            priceKind: "monthly",
            priceUsd: 99m,
            featureKeys: new[]
            {
                "PRICING.PREMIUM.F1", "PRICING.PREMIUM.F2", "PRICING.PREMIUM.F3",
                "PRICING.PREMIUM.F4", "PRICING.PREMIUM.F5"
            },
            ctaKey: "PRICING.PREMIUM.CTA",
            isRecommended: true,
            displayOrder: 3,
            listingLimit: 250));

        await EnsureAsync(context, Plan.Create(
            tier: "elite",
            nameKey: "PRICING.ELITE.NAME",
            taglineKey: "PRICING.ELITE.TAGLINE",
            priceKind: "contact",
            priceUsd: null,
            featureKeys: new[] { "PRICING.ELITE.F1", "PRICING.ELITE.F2", "PRICING.ELITE.F3" },
            ctaKey: "PRICING.ELITE.CTA",
            isRecommended: false,
            displayOrder: 4,
            listingLimit: null)); // "يُتفق عليه" — negotiated; no cap until a real Subscription
                                  // entity can carry a per-account agreed number.

        await context.SaveChangesAsync();
    }

    private static async Task EnsureAsync(AppDbContext context, Plan plan)
    {
        var exists = await context.Plans.AnyAsync(p => p.Tier == plan.Tier);
        if (exists)
            return;

        context.Plans.Add(plan);
    }
}
