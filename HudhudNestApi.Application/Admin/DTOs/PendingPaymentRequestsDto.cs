namespace HudhudNestApi.Application.Admin.DTOs;

/// <summary>A user's paid-plan request waiting for an administrator to confirm payment.</summary>
public sealed record PendingPlanRequestDto(
    Guid RequestId,
    Guid UserId,
    string UserName,
    string? Email,
    string Tier,
    decimal? PriceUsd,
    DateTime RequestedAt);

/// <summary>An owner's featured-placement fee waiting for an administrator to confirm payment.</summary>
public sealed record PendingFeaturedRequestDto(
    Guid TransactionId,
    Guid PropertyId,
    string PropertyTitle,
    Guid OwnerId,
    string OwnerName,
    string? OwnerEmail,
    decimal AmountUsd,
    DateTime RequestedAt);

/// <summary>Everything an administrator has to confirm payment for, in one read.</summary>
public sealed record PendingPaymentRequestsDto(
    IReadOnlyList<PendingPlanRequestDto> Plans,
    IReadOnlyList<PendingFeaturedRequestDto> Featured);
