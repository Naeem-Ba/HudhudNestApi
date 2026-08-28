using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Application.Services.DTOs;

public sealed record ServiceOfferingDto(
    Guid Id,
    Guid ServiceProviderId,
    string ServiceProviderDisplayName,
    ServiceProviderVerificationLevel ServiceProviderVerificationLevel,
    ServiceCategory Category,
    string Title,
    string? Description,
    decimal? BasePrice,
    int? CurrencyId,
    int? EstimatedDurationDays,
    bool IsActive,
    DateTime CreatedAt);
