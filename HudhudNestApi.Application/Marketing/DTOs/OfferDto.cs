namespace HudhudNestApi.Application.Marketing.DTOs;

/// <summary>
/// Public shape of the currently active Offer, e.g. "first 100 agencies" — returned by
/// <c>GET /api/offers/active</c>. Never includes <c>Status</c> or <c>StartsAtUtc</c>: only
/// an offer that is already active and currently redeemable is ever mapped to this DTO
/// (see GetActiveOfferQueryHandler), so there is nothing conditional left for the client to
/// interpret — if this DTO exists, the offer is live, full stop.
/// </summary>
public sealed record OfferDto(
    Guid Id,
    string Name,
    string? Description,
    string DiscountType,
    decimal DiscountValue,
    string? TargetPlanTier,
    DateTime? EndsAtUtc,
    int? MaxRedemptions,
    int RedeemedCount,
    string? Terms);
