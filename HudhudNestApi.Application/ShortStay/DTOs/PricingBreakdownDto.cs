namespace HudhudNestApi.Application.ShortStay.DTOs;

public sealed record NightlyPriceDto(DateOnly Date, decimal Price, string AppliedRuleType);

/// <summary>
/// Server-computed, itemized price breakdown for a candidate stay. Always built by
/// IPricingCalculationService — the frontend never computes the final amount itself,
/// it only displays this breakdown (spec §18: "لا تعتمد على Frontend لحساب المبلغ النهائي").
/// </summary>
public sealed record PricingBreakdownDto(
    IReadOnlyList<NightlyPriceDto> Nights,
    decimal NightsSubtotal,
    decimal CleaningFee,
    decimal ExtraGuestFee,
    decimal ExtraBedFee,
    decimal TotalAmount,
    int MinimumNightsRequired);
