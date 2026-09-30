using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class DistributionRuleTests
{
    private static DistributionRule MakeRule(
        int? provinceId = 1, int? propertyTypeId = 2, ListingType? transactionType = ListingType.ForSale, int priority = 100) =>
        DistributionRule.Create(
            "شقق للبيع في طرطوس", "وصف", provinceId, propertyTypeId, transactionType,
            Guid.NewGuid(), priority, null, null, Guid.NewGuid());

    [Fact]
    public void Create_ValidInput_StartsActiveAndNotArchived()
    {
        var rule = MakeRule();

        Assert.True(rule.IsActive);
        Assert.False(rule.IsArchived);
        Assert.Equal(3, rule.SpecificityScore);
    }

    [Fact]
    public void Create_AllDimensionsNull_IsAGeneralRule_WithZeroSpecificity()
    {
        var rule = DistributionRule.Create("قاعدة عامة", null, null, null, null, Guid.NewGuid(), 10, null, null, null);

        Assert.Null(rule.ProvinceId);
        Assert.Null(rule.PropertyTypeId);
        Assert.Null(rule.TransactionType);
        Assert.Equal(0, rule.SpecificityScore);
    }

    [Fact]
    public void Create_BlankName_Throws() =>
        Assert.Throws<DomainException>(() => DistributionRule.Create(" ", null, null, null, null, Guid.NewGuid(), 0, null, null, null));

    [Fact]
    public void Create_EmptySocialAccountId_Throws() =>
        Assert.Throws<DomainException>(() => DistributionRule.Create("اسم", null, null, null, null, Guid.Empty, 0, null, null, null));

    [Fact]
    public void Create_NegativePriority_Throws() =>
        Assert.Throws<DomainException>(() => DistributionRule.Create("اسم", null, null, null, null, Guid.NewGuid(), -1, null, null, null));

    [Fact]
    public void Create_StartAtAfterEndAt_Throws()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<DomainException>(() =>
            DistributionRule.Create("اسم", null, null, null, null, Guid.NewGuid(), 0, now, now.AddDays(-1), null));
    }

    [Fact]
    public void Create_InvalidProvinceOrPropertyTypeId_Throws()
    {
        Assert.Throws<DomainException>(() => DistributionRule.Create("اسم", null, 0, null, null, Guid.NewGuid(), 0, null, null, null));
        Assert.Throws<DomainException>(() => DistributionRule.Create("اسم", null, null, -1, null, Guid.NewGuid(), 0, null, null, null));
    }

    [Fact]
    public void Deactivate_ThenActivate_RoundTrips()
    {
        var rule = MakeRule();

        rule.Deactivate();
        Assert.False(rule.IsActive);

        rule.Activate();
        Assert.True(rule.IsActive);
    }

    [Fact]
    public void Archive_IsTerminal_BlocksFurtherMutation()
    {
        var rule = MakeRule();
        rule.Archive(Guid.NewGuid());

        Assert.True(rule.IsArchived);
        Assert.False(rule.IsActive);

        Assert.Throws<InvalidStateTransitionException>(() => rule.Activate());
        Assert.Throws<InvalidStateTransitionException>(() => rule.Deactivate());
        Assert.Throws<InvalidStateTransitionException>(() =>
            rule.Update("اسم جديد", null, null, null, null, 1, null, null, null));
    }

    [Fact]
    public void Update_ChangesCriteriaAndPriority()
    {
        var rule = MakeRule();

        rule.Update("اسم محدث", "وصف جديد", 5, 6, ListingType.ForRent, 200, null, null, Guid.NewGuid());

        Assert.Equal("اسم محدث", rule.Name);
        Assert.Equal(5, rule.ProvinceId);
        Assert.Equal(6, rule.PropertyTypeId);
        Assert.Equal(ListingType.ForRent, rule.TransactionType);
        Assert.Equal(200, rule.Priority);
    }

    [Theory]
    [InlineData(-1, 1, true)]   // started yesterday relative to now -> within window
    [InlineData(1, -1, false)]  // starts tomorrow -> not yet valid
    public void IsWithinValidityWindow_RespectsStartAndEndBounds(int startOffsetDays, int endOffsetDays, bool expected)
    {
        var now = DateTime.UtcNow;
        var rule = DistributionRule.Create(
            "اسم", null, null, null, null, Guid.NewGuid(), 0,
            now.AddDays(startOffsetDays), now.AddDays(Math.Max(startOffsetDays, endOffsetDays) + 2), null);

        Assert.Equal(expected, rule.IsWithinValidityWindow(now));
    }

    [Fact]
    public void IsWithinValidityWindow_NoBounds_AlwaysTrue()
    {
        var rule = DistributionRule.Create("اسم", null, null, null, null, Guid.NewGuid(), 0, null, null, null);
        Assert.True(rule.IsWithinValidityWindow(DateTime.UtcNow));
        Assert.True(rule.IsWithinValidityWindow(DateTime.UtcNow.AddYears(10)));
    }
}
