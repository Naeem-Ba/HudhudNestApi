namespace PropertyApi.Application.Marketing.DTOs;

/// <summary>Admin-only projection.</summary>
public sealed record SurveyResponseDto(
    Guid Id,
    Guid? LeadId,
    string? WillingnessToPay,
    string? PreferredPaymentModel,
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
    string Source,
    DateTime CreatedAt);
