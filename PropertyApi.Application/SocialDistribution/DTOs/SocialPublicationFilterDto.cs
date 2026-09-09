using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

public sealed record SocialPublicationFilterDto(
    Guid? PropertyId,
    Guid? SocialAccountId,
    SocialPlatform? Platform,
    SocialPublicationStatus? Status,
    DateTime? FromDate,
    DateTime? ToDate,
    int Page = 1,
    int PageSize = 20);
