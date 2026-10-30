using FluentValidation;
using MediatR;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Marketing.Commands.SubmitSurvey;

public sealed record SubmitSurveyCommand(
    Guid? LeadId,
    PaymentWillingness? WillingnessToPay,
    PreferredPaymentModel? PreferredPaymentModel,
    decimal? ExpectedMonthlyPriceUsd,
    decimal? ExpectedPerListingPriceUsd,
    decimal? AcceptableCommissionPercent,
    string? MostImportantFeature,
    string? BiggestProblem,
    string? SubscriptionBlocker,
    bool? WantsTrialBeforePaying,
    int? TeamSize,
    int? PropertyCount,
    bool? UsesSimilarToolCurrently,
    string? SimilarToolName,
    string Source) : IRequest<Guid>;

public sealed class SubmitSurveyCommandValidator : AbstractValidator<SubmitSurveyCommand>
{
    public SubmitSurveyCommandValidator()
    {
        // An unknown enum value (for example 99) was stored as is (QA Phase 12, P12-D10). An empty
        // answer set stays valid on purpose: see SurveyResponse.
        RuleFor(x => x.WillingnessToPay).IsInEnum().When(x => x.WillingnessToPay.HasValue);
        RuleFor(x => x.PreferredPaymentModel).IsInEnum().When(x => x.PreferredPaymentModel.HasValue);
    }
}
