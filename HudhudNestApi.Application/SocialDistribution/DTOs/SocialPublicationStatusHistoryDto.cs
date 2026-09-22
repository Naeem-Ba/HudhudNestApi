using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

public sealed record SocialPublicationStatusHistoryDto(
    Guid Id,
    SocialPublicationStatus? FromStatus,
    SocialPublicationStatus ToStatus,
    Guid? ChangedByUserId,
    string? Note,
    DateTime CreatedAt);
