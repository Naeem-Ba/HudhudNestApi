namespace HudhudNestApi.Application.Services.DTOs;

public sealed record ServiceReviewDto(
    Guid Id,
    Guid ServiceRequestId,
    Guid ServiceProviderId,
    Guid ReviewerId,
    string ReviewerName,
    int Rating,
    string? Comment,
    DateTime CreatedAt);
