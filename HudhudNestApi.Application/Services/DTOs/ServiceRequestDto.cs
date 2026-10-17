using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.DTOs;

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
    DateTime CreatedAt,
    DateTime? UpdatedAt = null,
    ServiceProviderContactDto? ProviderContact = null,
    ServiceOutcomeReason? OutcomeReason = null,
    string? OutcomeNote = null,
    DateTime? NotCompletedAt = null,
    ServiceRequesterAnswer? RequesterAnswer = null,
    ServiceRequesterReason? RequesterReason = null,
    string? RequesterFeedbackNote = null,
    DateTime? RequesterFeedbackAt = null);

/// <summary>
/// How to reach the provider. Present ONLY on a request DTO returned to its own requester (or an
/// admin) while <see cref="Domain.Services.Entities.ServiceRequest.ProviderContactVisible"/> holds.
/// Never part of a public offering or provider DTO.
/// </summary>
public sealed record ServiceProviderContactDto(string? Phone, string? Email);
