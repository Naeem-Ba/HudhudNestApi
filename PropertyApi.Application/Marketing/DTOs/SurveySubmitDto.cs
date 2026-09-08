using System.ComponentModel.DataAnnotations;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Marketing.DTOs;

/// <summary>Public request body for <c>POST /api/surveys/landing</c>. Every answer field is
/// optional — see SurveyResponse's class doc comment for why.</summary>
public sealed record SurveySubmitDto(
    Guid? LeadId,
    PaymentWillingness? WillingnessToPay,
    PreferredPaymentModel? PreferredPaymentModel,
    [Range(0, 1_000_000)] decimal? ExpectedMonthlyPriceUsd,
    [Range(0, 1_000_000)] decimal? ExpectedPerListingPriceUsd,
    [Range(0, 100)] decimal? AcceptableCommissionPercent,
    [StringLength(300)] string? MostImportantFeature,
    [StringLength(500)] string? BiggestProblem,
    [StringLength(500)] string? SubscriptionBlocker,
    bool? WantsTrialBeforePaying,
    [Range(0, 100_000)] int? TeamSize,
    [Range(0, 1_000_000)] int? PropertyCount,
    bool? UsesSimilarToolCurrently,
    [StringLength(150)] string? SimilarToolName);
