namespace HudhudNestApi.Application.Reviews.DTOs;

public sealed record AgencyRatingDto(
    Guid Id,
    Guid AgencyId,
    Guid RaterId,
    string RaterName,
    string? RaterImageUrl,
    int Service,
    int Professionalism,
    int Reliability,
    int OverallExperience,
    double OverallScore,
    string? Comment,
    DateTime CreatedAt);

public sealed record AgencyRatingSummaryDto(
    double AverageService,
    double AverageProfessionalism,
    double AverageReliability,
    double AverageOverallExperience,
    double AverageOverall,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<AgencyRatingDto> Ratings);

/// <summary>Whether the current user may rate the agency now, and why not.</summary>
public sealed record AgencyRatingEligibilityDto(
    bool CanRate,
    bool IsMemberOrOwner,
    bool HasInteraction,
    bool AlreadyRated,
    AgencyRatingDto? ExistingRating);
