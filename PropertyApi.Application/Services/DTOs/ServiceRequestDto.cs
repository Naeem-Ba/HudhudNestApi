using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Application.Services.DTOs;

public sealed record ServiceRequestDto(
    Guid Id,
    string RequestNumber,
    Guid PropertyId,
    string PropertyTitle,
    string? PropertyMainImageUrl,
    Guid RequesterId,
    string RequesterName,
    Guid ServiceProviderId,
    string ServiceProviderDisplayName,
    Guid ServiceOfferingId,
    string ServiceOfferingTitle,
    ServiceCategory Category,
    ServiceRequestStatus Status,
    string? RequesterNote,
    string? ProviderNote,
    DateTime? ScheduledAt,
    decimal? QuotedPrice,
    int? QuotedPriceCurrencyId,
    decimal? FinalPrice,
    int? FinalPriceCurrencyId,
    string? RejectionReason,
    string? CancellationReason,
    DateTime? CompletedAt,
    DateTime CreatedAt);
