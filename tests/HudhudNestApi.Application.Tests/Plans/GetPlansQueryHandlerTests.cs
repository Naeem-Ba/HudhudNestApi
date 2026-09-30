using Moq;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Plans.Queries.GetPlans;
using HudhudNestApi.Domain.Plans.Entities;

namespace HudhudNestApi.Application.Tests.Plans;

public sealed class GetPlansQueryHandlerTests
{
    [Fact]
    public async Task Handle_MapsRepositoryPlansToContractShapedDtos()
    {
        var free = Plan.Create(
            tier: "free",
            nameKey: "PRICING.FREE.NAME",
            taglineKey: "PRICING.FREE.TAGLINE",
            priceKind: "free",
            priceUsd: 0m,
            featureKeys: new[] { "PRICING.FREE.F1", "PRICING.FREE.F2" },
            ctaKey: "PRICING.FREE.CTA",
            isRecommended: false,
            displayOrder: 1,
            listingLimit: 1,
            noteKey: "PRICING.FREE.NOTE");

        var elite = Plan.Create(
            tier: "elite",
            nameKey: "PRICING.ELITE.NAME",
            taglineKey: "PRICING.ELITE.TAGLINE",
            priceKind: "contact",
            priceUsd: null,
            featureKeys: new[] { "PRICING.ELITE.F1" },
            ctaKey: "PRICING.ELITE.CTA",
            isRecommended: false,
            displayOrder: 4,
            listingLimit: null);

        var plans = new Mock<IPlanRepository>();
        plans
            .Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { free, elite });

        var handler = new GetPlansQueryHandler(plans.Object);

        var result = await handler.Handle(new GetPlansQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);

        var freeDto = result.Single(p => p.Tier == "free");
        Assert.Equal("PRICING.FREE.NAME", freeDto.NameKey);
        Assert.Equal("free", freeDto.PriceKind);
        Assert.Equal(0m, freeDto.PriceUsd);
        Assert.Equal(new[] { "PRICING.FREE.F1", "PRICING.FREE.F2" }, freeDto.FeatureKeys);
        Assert.Equal("PRICING.FREE.NOTE", freeDto.NoteKey);
        Assert.Equal(1, freeDto.ListingLimit);

        var eliteDto = result.Single(p => p.Tier == "elite");
        Assert.Equal("contact", eliteDto.PriceKind);
        Assert.Null(eliteDto.PriceUsd);
        Assert.Null(eliteDto.NoteKey);

        // /pricing must not invent its own "unlimited" display value — it reflects exactly
        // what the backend enforces (BACKEND-ISSUES.md §B-3, §14 pricing consistency).
        Assert.Null(eliteDto.ListingLimit);
    }
}
