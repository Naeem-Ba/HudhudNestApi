using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

public sealed record SocialPostContentDto(
    Guid Id,
    string Title,
    string Body,
    string ImageUrl,
    string TargetUrl,
    IReadOnlyList<string> Hashtags,
    string Language,
    int ContentVersion,
    ContentReviewStatus ReviewStatus,
    string? ReviewNote);
