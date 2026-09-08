using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Marketing.Entities;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Tests.Marketing;

public sealed class OfferTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithPercentageOver100_Throws()
    {
        Assert.Throws<DomainException>(() => Offer.Create(
            "أول 100 مكتب", OfferDiscountType.Percentage, 150m, Now));
    }

    [Fact]
    public void Create_WithZeroOrNegativeDiscountValue_Throws()
    {
        Assert.Throws<DomainException>(() => Offer.Create(
            "أول 100 مكتب", OfferDiscountType.Percentage, 0m, Now));
    }

    [Fact]
    public void Create_WithEndDateBeforeStartDate_Throws()
    {
        Assert.Throws<DomainException>(() => Offer.Create(
            "أول 100 مكتب",
            OfferDiscountType.Percentage,
            20m,
            Now,
            endsAtUtc: Now.AddDays(-1)));
    }

    [Fact]
    public void Create_WithZeroMaxRedemptions_Throws()
    {
        Assert.Throws<DomainException>(() => Offer.Create(
            "أول 100 مكتب", OfferDiscountType.Percentage, 20m, Now, maxRedemptions: 0));
    }

    [Fact]
    public void Create_StartsInDraftStatus()
    {
        var offer = Offer.Create("أول 100 مكتب", OfferDiscountType.Percentage, 20m, Now, maxRedemptions: 100);

        Assert.Equal(OfferStatus.Draft, offer.Status);
        Assert.Equal(0, offer.RedeemedCount);
        Assert.False(offer.IsCurrentlyRedeemable(Now));
    }

    [Fact]
    public void Activate_ThenIsCurrentlyRedeemable_WithinWindowAndUnderCap_ReturnsTrue()
    {
        var offer = Offer.Create(
            "أول 100 مكتب", OfferDiscountType.Percentage, 20m, Now, maxRedemptions: 100);
        offer.Activate();

        Assert.True(offer.IsCurrentlyRedeemable(Now));
    }

    [Fact]
    public void IsCurrentlyRedeemable_AfterEndDate_ReturnsFalse()
    {
        var offer = Offer.Create(
            "أول 100 مكتب",
            OfferDiscountType.Percentage,
            20m,
            Now,
            endsAtUtc: Now.AddDays(30));
        offer.Activate();

        Assert.False(offer.IsCurrentlyRedeemable(Now.AddDays(31)));
    }

    [Fact]
    public void Activate_WhenEnded_Throws()
    {
        var offer = Offer.Create("أول 100 مكتب", OfferDiscountType.Percentage, 20m, Now);
        offer.Activate();
        offer.End();

        Assert.Throws<DomainException>(() => offer.Activate());
    }

    [Fact]
    public void Pause_WhenNotActive_Throws()
    {
        var offer = Offer.Create("أول 100 مكتب", OfferDiscountType.Percentage, 20m, Now);

        Assert.Throws<DomainException>(() => offer.Pause());
    }

    [Fact]
    public void UpdateDetails_RaisingMaxRedemptions_Succeeds()
    {
        // RedeemedCount only ever changes through the repository's atomic UPDATE (see
        // Offer's class doc comment), so it is always 0 on a freshly created in-memory
        // instance — this test can only exercise the "raise the cap" side of the guard.
        // The "reject lowering it below what was actually redeemed" side needs a genuine
        // non-zero RedeemedCount and is covered by OffersControllerTests (integration),
        // which redeems a real slot before attempting to lower the cap under it.
        var offer = Offer.Create(
            "أول 100 مكتب", OfferDiscountType.Percentage, 20m, Now, maxRedemptions: 10);

        offer.UpdateDetails(
            "أول 100 مكتب", OfferDiscountType.Percentage, 20m, Now,
            description: null, targetPlanTier: null, endsAtUtc: null, maxRedemptions: 200, terms: null);

        Assert.Equal(200, offer.MaxRedemptions);
    }
}
