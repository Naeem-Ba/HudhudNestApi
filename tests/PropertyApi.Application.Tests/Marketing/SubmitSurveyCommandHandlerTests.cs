using Moq;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Marketing.Commands.SubmitSurvey;
using PropertyApi.Application.Marketing.Interfaces;
using PropertyApi.Domain.Marketing.Entities;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Tests.Marketing;

public sealed class SubmitSurveyCommandHandlerTests
{
    [Fact]
    public async Task Handle_PersistsResponseAndSaves()
    {
        var surveys = new Mock<ISurveyResponseRepository>();
        SurveyResponse? added = null;
        surveys.Setup(x => x.Add(It.IsAny<SurveyResponse>())).Callback<SurveyResponse>(r => added = r);

        var uow = new Mock<IUnitOfWork>();
        var handler = new SubmitSurveyCommandHandler(surveys.Object, uow.Object);

        var leadId = Guid.NewGuid();
        var result = await handler.Handle(
            new SubmitSurveyCommand(
                leadId,
                PaymentWillingness.Maybe,
                PreferredPaymentModel.CommissionOnSale,
                ExpectedMonthlyPriceUsd: null,
                ExpectedPerListingPriceUsd: null,
                AcceptableCommissionPercent: 3m,
                MostImportantFeature: "التقارير",
                BiggestProblem: null,
                SubscriptionBlocker: null,
                WantsTrialBeforePaying: true,
                TeamSize: 3,
                PropertyCount: 40,
                UsesSimilarToolCurrently: null,
                SimilarToolName: null,
                Source: "landing-page"),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(result, added!.Id);
        Assert.Equal(leadId, added.LeadId);
        Assert.Equal(PaymentWillingness.Maybe, added.WillingnessToPay);
        surveys.Verify(x => x.Add(It.IsAny<SurveyResponse>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
