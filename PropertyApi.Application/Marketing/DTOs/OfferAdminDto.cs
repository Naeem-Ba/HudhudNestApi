namespace PropertyApi.Application.Marketing.DTOs;

/// <summary>Admin listing/editing shape — includes lifecycle fields deliberately withheld
/// from the public <see cref="OfferDto"/> (Status, StartsAtUtc).</summary>
public sealed record OfferAdminDto(
    Guid Id,
    string Name,
    string? Description,
    string DiscountType,
    decimal DiscountValue,
    string? TargetPlanTier,
    DateTime StartsAtUtc,
    DateTime? EndsAtUtc,
    int? MaxRedemptions,
    int RedeemedCount,
    string Status,
    string? Terms,
    DateTime CreatedAt,
    DateTime UpdatedAt);
