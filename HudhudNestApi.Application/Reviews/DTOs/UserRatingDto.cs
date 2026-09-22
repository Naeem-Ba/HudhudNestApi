namespace HudhudNestApi.Application.Reviews.DTOs;

public sealed record UserRatingDto(
    Guid Id,
    Guid RatedUserId,
    Guid RaterId,
    string RaterName,
    string? RaterImageUrl,
    int Credibility,
    int Safety,
    int ResponseSpeed,
    int Transparency,
    double OverallScore,
    string? Comment,
    DateTime CreatedAt
);

public sealed record UserRatingSummaryDto(
    double AverageCredibility,
    double AverageSafety,
    double AverageResponseSpeed,
    double AverageTransparency,
    double AverageOverall,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<UserRatingDto> Ratings
);

/// <summary>
/// GET /api/Users/{id}/ratings/eligibility — يخبر الواجهة الأمامية بحالة
/// المستخدم الحالي تجاه تقييم هذا الحساب: هل يحق له التقييم أصلًا (زيارة
/// مكتملة أو رسائل متبادلة)، وإن كان قد قيّمه من قبل (لعرض نموذج التعديل
/// مُعبَّأً مسبقًا بدل نموذج فارغ).
/// </summary>
public sealed record RatingEligibilityDto(
    bool CanRate,
    bool IsSelf,
    bool AlreadyRated,
    UserRatingDto? ExistingRating
);
