namespace PropertyApi.Domain.ShortStay.Enums;

/// <summary>
/// Priority when multiple rules overlap the same night (highest first, decided and
/// enforced in PricingCalculationService — NOT by this enum's numeric value):
/// Holiday &gt; Seasonal &gt; Weekend &gt; Weekday &gt; Base.
/// </summary>
public enum PricingRuleType
{
    Base = 0,
    Weekday = 1,
    Weekend = 2,
    Seasonal = 3,
    Holiday = 4,
    CustomDate = 5,
}
