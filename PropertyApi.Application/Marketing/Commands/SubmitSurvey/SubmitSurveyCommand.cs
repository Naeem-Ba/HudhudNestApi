using MediatR;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Marketing.Commands.SubmitSurvey;

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
