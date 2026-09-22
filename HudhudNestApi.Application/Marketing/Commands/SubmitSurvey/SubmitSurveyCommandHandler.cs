using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Commands.SubmitSurvey;

public sealed class SubmitSurveyCommandHandler : IRequestHandler<SubmitSurveyCommand, Guid>
{
    private readonly ISurveyResponseRepository _surveys;
    private readonly IUnitOfWork _uow;

    public SubmitSurveyCommandHandler(ISurveyResponseRepository surveys, IUnitOfWork uow)
    {
        _surveys = surveys;
        _uow = uow;
    }

    public async Task<Guid> Handle(SubmitSurveyCommand request, CancellationToken cancellationToken)
    {
        var response = SurveyResponse.Create(
            request.Source,
            request.LeadId,
            request.WillingnessToPay,
            request.PreferredPaymentModel,
            request.ExpectedMonthlyPriceUsd,
            request.ExpectedPerListingPriceUsd,
            request.AcceptableCommissionPercent,
            request.MostImportantFeature,
            request.BiggestProblem,
            request.SubscriptionBlocker,
            request.WantsTrialBeforePaying,
            request.TeamSize,
            request.PropertyCount,
            request.UsesSimilarToolCurrently,
            request.SimilarToolName);

        _surveys.Add(response);
        await _uow.SaveChangesAsync(cancellationToken);

        return response.Id;
    }
}
