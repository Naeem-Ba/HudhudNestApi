using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.Commands.SubmitSurvey;

public sealed class SubmitSurveyCommandHandler : IRequestHandler<SubmitSurveyCommand, Guid>
{
    private readonly ISurveyResponseRepository _surveys;
    private readonly IUnitOfWork _uow;
    private readonly ILeadRepository _leads;

    public SubmitSurveyCommandHandler(ISurveyResponseRepository surveys, IUnitOfWork uow, ILeadRepository leads)
    {
        _surveys = surveys;
        _uow = uow;
        _leads = leads;
    }

    public async Task<Guid> Handle(SubmitSurveyCommand request, CancellationToken cancellationToken)
    {
        // Launch audit A2-003: a public form; an unknown lead id reached the foreign key as a 500.
        // The answers are kept, the dangling link is dropped.
        var leadId = request.LeadId is { } lead && await _leads.GetByIdAsync(lead, cancellationToken) is not null
            ? request.LeadId
            : null;

        var response = SurveyResponse.Create(
            request.Source,
            leadId,
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
