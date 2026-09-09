using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

public sealed record SocialPublicationDeadLetterDto(
    Guid Id,
    Guid PublicationId,
    Guid SocialAccountId,
    SocialPlatform Platform,
    SocialPublicationErrorCode LastErrorCode,
    string LastErrorMessage,
    int Attempts,
    DateTime FailedAt,
    DateTime? ResolvedAt,
    Guid? ResolvedByUserId,
    string? ResolutionNote);
