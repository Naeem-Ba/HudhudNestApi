using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Domain.SocialDistribution.Services;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class DistributionRuleEvaluatorTests
{
    private static readonly Guid TartusAccount = Guid.NewGuid();
    private static readonly DateTime UtcNow = DateTime.UtcNow;

    private static DistributionRule MakeRule(
        int? provinceId, int? propertyTypeId, ListingType? transactionType, int priority = 0,
        Guid? socialAccountId = null, DateTime? startAt = null, DateTime? endAt = null) =>
        DistributionRule.Create(
            "قاعدة اختبار", null, provinceId, propertyTypeId, transactionType,
            socialAccountId ?? TartusAccount, priority, startAt, endAt, null);

    private static DistributionCandidateProperty MakeProperty(int? governorateId = 1, int? propertyTypeId = 2, ListingType listingType = ListingType.ForSale) =>
        new(Guid.NewGuid(), governorateId, propertyTypeId, listingType);

    [Fact]
    public void Matches_AllDimensionsExactMatch_ReturnsTrue()
    {
        var rule = MakeRule(1, 2, ListingType.ForSale);
        var property = MakeProperty(1, 2, ListingType.ForSale);

        Assert.True(DistributionRuleEvaluator.Matches(rule, property, UtcNow));
    }

    [Fact]
    public void Matches_AllDimensionsNull_MatchesEveryProperty()
    {
        var rule = MakeRule(null, null, null);

        Assert.True(DistributionRuleEvaluator.Matches(rule, MakeProperty(1, 2, ListingType.ForSale), UtcNow));
        Assert.True(DistributionRuleEvaluator.Matches(rule, MakeProperty(99, 42, ListingType.ForRent), UtcNow));
        Assert.True(DistributionRuleEvaluator.Matches(rule, MakeProperty(null, null, ListingType.ForRentAndSale), UtcNow));
    }

    [Fact]
    public void Matches_ProvinceMismatch_ReturnsFalse()
    {
        var rule = MakeRule(1, null, null);
        Assert.False(DistributionRuleEvaluator.Matches(rule, MakeProperty(2, 2, ListingType.ForSale), UtcNow));
    }

    [Fact]
    public void Matches_PropertyTypeMismatch_ReturnsFalse()
    {
        var rule = MakeRule(null, 2, null);
        Assert.False(DistributionRuleEvaluator.Matches(rule, MakeProperty(1, 3, ListingType.ForSale), UtcNow));
    }

    [Fact]
    public void Matches_TransactionTypeMismatch_ReturnsFalse()
    {
        var rule = MakeRule(null, null, ListingType.ForSale);
        Assert.False(DistributionRuleEvaluator.Matches(rule, MakeProperty(1, 2, ListingType.ForRent), UtcNow));
    }

    [Fact]
    public void Matches_PropertyWithNullProvince_NeverMatchesAProvinceSpecificRule()
    {
        var rule = MakeRule(1, null, null);
        var property = MakeProperty(governorateId: null);

        Assert.False(DistributionRuleEvaluator.Matches(rule, property, UtcNow));
    }

    [Fact]
    public void Matches_PropertyWithNullPropertyType_NeverMatchesATypeSpecificRule()
    {
        var rule = MakeRule(null, 2, null);
        var property = MakeProperty(propertyTypeId: null);

        Assert.False(DistributionRuleEvaluator.Matches(rule, property, UtcNow));
    }

    [Fact]
    public void Matches_InactiveRule_ReturnsFalse()
    {
        var rule = MakeRule(null, null, null);
        rule.Deactivate();

        Assert.False(DistributionRuleEvaluator.Matches(rule, MakeProperty(), UtcNow));
    }

    [Fact]
    public void Matches_ArchivedRule_ReturnsFalse()
    {
        var rule = MakeRule(null, null, null);
        rule.Archive(null);

        Assert.False(DistributionRuleEvaluator.Matches(rule, MakeProperty(), UtcNow));
    }

    [Fact]
    public void Matches_ExpiredValidityWindow_ReturnsFalse()
    {
        var rule = MakeRule(null, null, null, startAt: UtcNow.AddDays(-10), endAt: UtcNow.AddDays(-1));
        Assert.False(DistributionRuleEvaluator.Matches(rule, MakeProperty(), UtcNow));
    }

    [Fact]
    public void Matches_FutureValidityWindow_ReturnsFalse()
    {
        var rule = MakeRule(null, null, null, startAt: UtcNow.AddDays(1));
        Assert.False(DistributionRuleEvaluator.Matches(rule, MakeProperty(), UtcNow));
    }

    [Fact]
    public void SelectWinner_HigherPriorityWins_RegardlessOfSpecificity()
    {
        var general = MakeRule(null, null, null, priority: 200); // low specificity, high priority
        var specific = MakeRule(1, 2, ListingType.ForSale, priority: 50); // high specificity, low priority

        var winner = DistributionRuleEvaluator.SelectWinner([general, specific]);

        Assert.Equal(general.Id, winner!.Id);
    }

    [Fact]
    public void SelectWinner_EqualPriority_MoreSpecificRuleWins()
    {
        var general = MakeRule(null, null, null, priority: 100);
        var specific = MakeRule(1, 2, ListingType.ForSale, priority: 100);

        var winner = DistributionRuleEvaluator.SelectWinner([general, specific]);

        Assert.Equal(specific.Id, winner!.Id);
    }

    [Fact]
    public void SelectWinner_EmptySet_ReturnsNull() =>
        Assert.Null(DistributionRuleEvaluator.SelectWinner([]));

    [Fact]
    public void SelectWinner_SingleRule_ReturnsIt()
    {
        var rule = MakeRule(1, 2, ListingType.ForSale);
        Assert.Equal(rule.Id, DistributionRuleEvaluator.SelectWinner([rule])!.Id);
    }
}
