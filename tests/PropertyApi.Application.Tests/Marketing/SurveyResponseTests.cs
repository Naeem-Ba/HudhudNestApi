using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Marketing.Entities;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Tests.Marketing;

public sealed class SurveyResponseTests
{
    [Fact]
    public void Create_WithNoAnswers_Succeeds()
    {
        // The survey is fully skippable by design — see the entity's class doc comment.
        var response = SurveyResponse.Create("landing-page");

        Assert.Equal("landing-page", response.Source);
        Assert.Null(response.WillingnessToPay);
        Assert.Null(response.PreferredPaymentModel);
        Assert.Null(response.LeadId);
    }

    [Fact]
    public void Create_WithoutSource_Throws()
    {
        Assert.Throws<DomainException>(() => SurveyResponse.Create(""));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Create_WithNegativeExpectedMonthlyPrice_Throws(decimal price)
    {
        Assert.Throws<DomainException>(() => SurveyResponse.Create(
            "landing-page", expectedMonthlyPriceUsd: price));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Create_WithCommissionPercentOutOfRange_Throws(decimal percent)
    {
        Assert.Throws<DomainException>(() => SurveyResponse.Create(
            "landing-page", acceptableCommissionPercent: percent));
    }

    [Fact]
    public void Create_WithNegativeTeamSize_Throws()
    {
        Assert.Throws<DomainException>(() => SurveyResponse.Create("landing-page", teamSize: -1));
    }

    [Fact]
    public void Create_WithFullAnswers_StoresAllFields()
    {
        var leadId = Guid.NewGuid();

        var response = SurveyResponse.Create(
            "landing-page",
            leadId: leadId,
            willingnessToPay: PaymentWillingness.Yes,
            preferredPaymentModel: PreferredPaymentModel.MonthlySubscription,
            expectedMonthlyPriceUsd: 49m,
            expectedPerListingPriceUsd: 5m,
            acceptableCommissionPercent: 2.5m,
            mostImportantFeature: "إدارة الإعلانات",
            biggestProblem: "تشتت البيانات",
            subscriptionBlocker: "السعر مرتفع",
            wantsTrialBeforePaying: true,
            teamSize: 5,
            propertyCount: 120,
            usesSimilarToolCurrently: false,
            similarToolName: null);

        Assert.Equal(leadId, response.LeadId);
        Assert.Equal(PaymentWillingness.Yes, response.WillingnessToPay);
        Assert.Equal(PreferredPaymentModel.MonthlySubscription, response.PreferredPaymentModel);
        Assert.Equal(49m, response.ExpectedMonthlyPriceUsd);
        Assert.Equal(5, response.TeamSize);
        Assert.Equal(120, response.PropertyCount);
        Assert.True(response.WantsTrialBeforePaying);
        Assert.False(response.UsesSimilarToolCurrently);
    }

    [Fact]
    public void Create_TruncatesOverlongFreeTextFields()
    {
        var response = SurveyResponse.Create(
            "landing-page",
            mostImportantFeature: new string('س', 400));

        Assert.Equal(300, response.MostImportantFeature!.Length);
    }
}
