namespace PropertyApi.Application.Reviews.DTOs;

public sealed record PropertyReviewDto(
    Guid Id,
    Guid PropertyId,
    Guid ReviewerId,
    string ReviewerName,
    string? ReviewerImageUrl,
    int Rating,
    string? Comment,
    DateTime CreatedAt
);

public sealed record PropertyReviewSummaryDto(
    double AverageRating,
    int TotalCount,
    IReadOnlyList<PropertyReviewDto> Reviews
);

