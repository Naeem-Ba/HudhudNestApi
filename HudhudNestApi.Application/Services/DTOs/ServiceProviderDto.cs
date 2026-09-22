using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.DTOs;

public sealed record ServiceProviderDto(
    Guid Id,
    Guid UserId,
    Guid? AgencyId,
    string DisplayName,
    string? Bio,
    string? LogoUrl,
    string? ContactEmail,
    string? ContactPhone,
    ServiceProviderVerificationLevel VerificationLevel,
    bool IsActive,
    double? AverageRating,
    int ReviewCount,
    DateTime CreatedAt);
