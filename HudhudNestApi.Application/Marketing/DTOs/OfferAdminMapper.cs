using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Application.Marketing.DTOs;

internal static class OfferAdminMapper
{
    public static OfferAdminDto ToDto(Offer offer, DateTime nowUtc) => new(
        offer.Id,
        offer.Name,
        offer.Description,
        offer.DiscountType.ToString(),
        offer.DiscountValue,
        offer.TargetPlanTier,
        offer.StartsAtUtc,
        offer.EndsAtUtc,
        offer.MaxRedemptions,
        offer.RedeemedCount,
        offer.Status.ToString(),
        offer.Terms,
        offer.CreatedAt,
        offer.UpdatedAt,
        offer.IsCurrentlyRedeemable(nowUtc));
}
