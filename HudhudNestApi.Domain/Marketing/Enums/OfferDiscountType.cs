namespace HudhudNestApi.Domain.Marketing.Enums;

/// <summary>How an Offer's <c>DiscountValue</c> should be interpreted. No billing system
/// consumes this yet (FRONTEND_BACKEND_CONTRACT.md §11.5) — today it is display-only
/// marketing copy plus the honoring commitment the sales team applies manually once a
/// lead converts.</summary>
public enum OfferDiscountType
{
    Percentage = 1,
    FixedAmountUsd = 2,
    FreeMonths = 3
}
