namespace HudhudNestApi.Application.ShortStay.DTOs;

public sealed record ShortStayReviewDto(
    Guid Id,
    Guid BookingId,
    Guid ReviewerId,
    Guid ShortStayListingId,
    int Rating,
    string? Comment,
    DateTime CreatedAt);
