namespace HudhudNestApi.Application.Agencies.Ratings.DTOs;

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

/// <summary>GET /api/agencies/{id}/ratings: per-criterion averages (0 when unrated) plus one page.</summary>
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

/// <summary>
/// GET /api/agencies/{id}/ratings/eligibility: what the signed-in caller may do. The server is the
/// source of truth; the client only shows the form when <see cref="CanRate"/> is true.
/// </summary>
public sealed record AgencyRatingEligibilityDto(
    bool CanRate,
    bool IsMemberOrOwner,
    bool HasInteraction,
    bool AlreadyRated,
    AgencyRatingDto? ExistingRating);
