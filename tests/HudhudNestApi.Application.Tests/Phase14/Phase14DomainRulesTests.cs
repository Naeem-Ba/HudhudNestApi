using HudhudNestApi.Application.Moderation;
using HudhudNestApi.Domain.AppUpdates.ValueObjects;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Tests.Phase14;

public sealed class Phase14DomainRulesTests
{
    [Theory(DisplayName = "D08: a version with a number beyond Int32 is invalid, never an exception")]
    [InlineData("99999999999.1.1")]
    [InlineData("1.99999999999.1")]
    [InlineData("1.1.2147483648")]
    public void AppVersion_Overflow_IsRejected(string input)
    {
        Assert.False(AppVersion.TryParse(input, out _));
    }

    [Fact(DisplayName = "D08: the largest valid version still parses")]
    public void AppVersion_MaxInt_Parses()
    {
        Assert.True(AppVersion.TryParse("2147483647.0.1", out var version));
        Assert.Equal(int.MaxValue, version.Major);
    }

    private static Offer Make(OfferDiscountType type = OfferDiscountType.Percentage, decimal value = 10, string? tier = "premium")
        => Offer.Create("Launch", type, value, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), targetPlanTier: tier);

    [Fact(DisplayName = "D11: an unknown plan tier, an undefined discount type or an absurd free period is refused")]
    public void Offer_RejectsUnknownValues()
    {
        Assert.Throws<DomainException>(() => Make(tier: "gold"));
        Assert.Throws<DomainException>(() => Make(type: (OfferDiscountType)9));
        Assert.Throws<DomainException>(() => Make(OfferDiscountType.FreeMonths, 9999));
        Assert.NotNull(Make(OfferDiscountType.FreeMonths, 6, "Elite"));
        Assert.NotNull(Make(tier: null));
    }

    [Fact(DisplayName = "D11: the same rules apply when an offer is edited")]
    public void Offer_UpdateRejectsUnknownTier()
    {
        var offer = Make();
        Assert.Throws<DomainException>(() => offer.UpdateDetails(
            "Launch", OfferDiscountType.Percentage, 10, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "gold", null, null, null));
    }

    [Fact(DisplayName = "Reports: the notification excerpt collapses whitespace and cuts long text")]
    public void ReportExcerpt()
    {
        Assert.Null(ListingReportNotifier.Excerpt(null));
        Assert.Null(ListingReportNotifier.Excerpt("   "));
        Assert.Equal("a b c", ListingReportNotifier.Excerpt("  a\n\nb   c "));
        var cut = ListingReportNotifier.Excerpt(new string('x', 500))!;
        Assert.Equal(201, cut.Length);
        Assert.EndsWith("…", cut);
    }
}
