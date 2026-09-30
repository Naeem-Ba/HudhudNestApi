using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.ShortStay.Interfaces;

public interface IPricingCalculationService
{
    /// <summary>
    /// Computes the full, itemized, server-authoritative price for a candidate stay.
    /// Throws DomainException if the stay is shorter than the applicable minimum-stay rule —
    /// callers must not let the frontend's own minimum-stay display be the only guard.
    /// </summary>
    PricingBreakdownDto Calculate(
        RoomType roomType,
        IReadOnlyList<PricingRule> pricingRules,
        IReadOnlyList<MinimumStayRule> minimumStayRules,
        decimal listingCleaningFee,
        decimal listingExtraGuestFee,
        decimal listingExtraBedFee,
        int listingCapacity,
        DateOnly checkIn,
        DateOnly checkOut,
        int countedGuests);
}
