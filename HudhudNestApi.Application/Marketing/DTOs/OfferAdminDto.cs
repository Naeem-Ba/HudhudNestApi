namespace HudhudNestApi.Application.Marketing.DTOs;

/// <summary>Admin listing/editing shape — includes lifecycle fields deliberately withheld
/// from the public <see cref="OfferDto"/> (Status, StartsAtUtc). <c>IsPubliclyVisible</c> is
/// the server's own verdict on "would a visitor see this offer right now" (Active, inside its
/// date window, cap not reached) so the admin UI never has to re-derive it.</summary>
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
    DateTime UpdatedAt,
    bool IsPubliclyVisible);
