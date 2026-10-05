namespace HudhudNestApi.Application.Listings.DTOs;

/// <summary>One of the owner's featured requests that an administrator has not confirmed yet.</summary>
public sealed record PendingFeaturedRequestSummaryDto(
    Guid TransactionId,
    Guid PropertyId,
    decimal AmountUsd,
    int FeaturedDays,
    DateTime RequestedAt);
