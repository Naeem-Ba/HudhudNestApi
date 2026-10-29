namespace HudhudNestApi.Application.ShortStay.DTOs;

public sealed record ShortStayReviewDto(
    Guid Id,
    Guid BookingId,
    Guid ReviewerId,
    Guid ShortStayListingId,
    int Rating,
    string? Comment,
    DateTime CreatedAt);

/// <summary>One review on a listing page.</summary>
public sealed record ShortStayReviewItemDto(
    Guid Id,
    Guid ReviewerId,
    string ReviewerName,
    string? ReviewerImageUrl,
    int Rating,
    string? Comment,
    DateTime CreatedAt);

public sealed record ShortStayReviewSummaryDto(
    double AverageRating,
    int TotalCount,
    IReadOnlyList<ShortStayReviewItemDto> Reviews);
