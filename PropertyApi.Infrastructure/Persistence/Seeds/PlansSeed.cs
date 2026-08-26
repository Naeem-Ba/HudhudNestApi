using Microsoft.EntityFrameworkCore;
using PropertyApi.Domain.Plans.Entities;

namespace PropertyApi.Infrastructure.Persistence.Seeds;

/// <summary>
/// Seeds the four tiers, mirroring the Wohnungsmieten frontend's
/// src/app/core/models/subscription-plan.model.ts SUBSCRIPTION_PLANS constant key
/// for key — the two must stay in sync, since neither owns display text (that's
/// entirely frontend i18n).
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
            displayOrder: 2));

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
            displayOrder: 3));

        await EnsureAsync(context, Plan.Create(
            tier: "elite",
            nameKey: "PRICING.ELITE.NAME",
            taglineKey: "PRICING.ELITE.TAGLINE",
            priceKind: "contact",
            priceUsd: null,
            featureKeys: new[] { "PRICING.ELITE.F1", "PRICING.ELITE.F2", "PRICING.ELITE.F3" },
            ctaKey: "PRICING.ELITE.CTA",
            isRecommended: false,
            displayOrder: 4));

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
