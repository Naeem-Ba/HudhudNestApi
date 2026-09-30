using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

public sealed record SocialAccountFilterDto(
    SocialPlatform? Platform,
    int? GovernorateId,
    SocialAccountStatus? Status,
    int Page = 1,
    int PageSize = 20);
