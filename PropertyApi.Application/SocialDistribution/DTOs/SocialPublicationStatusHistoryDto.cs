using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

public sealed record SocialPublicationStatusHistoryDto(
    Guid Id,
    SocialPublicationStatus? FromStatus,
    SocialPublicationStatus ToStatus,
    Guid? ChangedByUserId,
    string? Note,
    DateTime CreatedAt);
