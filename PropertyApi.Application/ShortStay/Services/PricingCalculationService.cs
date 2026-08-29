using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;

namespace PropertyApi.Application.ShortStay.Services;

/// <summary>
/// Stateless, DB-free pricing engine — deliberately kept free of any repository dependency so
/// it can be unit-tested directly (see plan's "الاختبارات" section).
///
/// Rule priority when several rules cover the same night (highest wins): CustomDate &gt;
/// Holiday &gt; Seasonal &gt; Weekend &gt; Weekday &gt; Base. This ranking is intentionally the same
/// order as PricingRuleType's own numeric values, so "highest enum value wins" is the whole
/// algorithm — documented here because the spec explicitly warns not to assume an order
/// without deciding and writing it down (§18: "لا تفترض هذه الأولوية دون فحص التصميم النهائي
/// وتوثيقها"). CustomDate outranks Holiday because it represents the host's own single-date
/// override, which should always beat a general seasonal/holiday rule.
///
/// ExtraBedFee is NOT charged automatically here — there is no "extra bed requested" input on
/// CreateBookingCommand yet, so charging it would be a guess. It is returned as 0 until that
/// input exists; see the plan's deferred-items list.
/// </summary>
public sealed class PricingCalculationService : IPricingCalculationService
{
    public PricingBreakdownDto Calculate(
        RoomType roomType,
        IReadOnlyList<PricingRule> pricingRules,
        IReadOnlyList<MinimumStayRule> minimumStayRules,
        decimal listingCleaningFee,
        decimal listingExtraGuestFee,
        decimal listingExtraBedFee,
        int listingCapacity,
        DateOnly checkIn,
        DateOnly checkOut,
        int countedGuests)
    {
        if (checkOut <= checkIn)
            throw new DomainException("تاريخ المغادرة يجب أن يكون بعد تاريخ الوصول.");

        var nights = checkOut.DayNumber - checkIn.DayNumber;
        var minimumRequired = ResolveMinimumStay(checkIn, minimumStayRules);

        if (nights < minimumRequired)
            throw new DomainException($"الحد الأدنى للإقامة هو {minimumRequired} ليلة/ليالٍ ابتداءً من هذا التاريخ.");

        var nightly = new List<NightlyPriceDto>(nights);
        decimal nightsSubtotal = 0m;

        for (var date = checkIn; date < checkOut; date = date.AddDays(1))
        {
            var rule = FindApplicableRule(date, pricingRules);
            var price = rule?.PricePerNight ?? roomType.BasePricePerNight;
            nightly.Add(new NightlyPriceDto(date, price, rule?.RuleType.ToString() ?? nameof(PricingRuleType.Base)));
            nightsSubtotal += price;
        }

        var capacity = roomType.Capacity ?? listingCapacity;
        var excessGuests = Math.Max(0, countedGuests - capacity);
        var extraGuestFee = excessGuests * listingExtraGuestFee * nights;

        var total = nightsSubtotal + listingCleaningFee + extraGuestFee;

        return new PricingBreakdownDto(
            Nights: nightly,
            NightsSubtotal: nightsSubtotal,
            CleaningFee: listingCleaningFee,
            ExtraGuestFee: extraGuestFee,
            ExtraBedFee: 0m,
            TotalAmount: total,
            MinimumNightsRequired: minimumRequired);
    }

    private static PricingRule? FindApplicableRule(DateOnly date, IReadOnlyList<PricingRule> rules)
    {
        PricingRule? best = null;

        foreach (var rule in rules)
        {
            var matches = rule.RuleType switch
            {
                PricingRuleType.CustomDate or PricingRuleType.Holiday or PricingRuleType.Seasonal =>
                    rule.DateRangeStart.HasValue && rule.DateRangeEnd.HasValue
                        && date >= rule.DateRangeStart.Value && date <= rule.DateRangeEnd.Value,
                PricingRuleType.Weekday or PricingRuleType.Weekend =>
                    rule.DayOfWeek.HasValue && rule.DayOfWeek.Value == date.DayOfWeek,
                PricingRuleType.Base => true,
                _ => false,
            };

            if (!matches)
                continue;

            if (best is null || rule.RuleType > best.RuleType)
                best = rule;
        }

        return best;
    }

    private static int ResolveMinimumStay(DateOnly checkIn, IReadOnlyList<MinimumStayRule> rules)
    {
        var defaultRule = rules.FirstOrDefault(r => r.DateRangeStart is null && r.DateRangeEnd is null);
        var applicable = rules
            .Where(r => r.DateRangeStart.HasValue && r.DateRangeEnd.HasValue
                && checkIn >= r.DateRangeStart!.Value && checkIn <= r.DateRangeEnd!.Value)
            .Select(r => r.MinimumNights)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(applicable, defaultRule?.MinimumNights ?? 1);
    }
}
