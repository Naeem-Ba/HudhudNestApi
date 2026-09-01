using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.ShortStay.Enums;

namespace PropertyApi.Domain.ShortStay.Entities;

/// <summary>
/// One pricing rule for a RoomType. Priority when multiple rules cover the same night
/// (highest wins, decided and enforced in PricingCalculationService, not here):
/// Holiday &gt; Seasonal &gt; Weekend &gt; Weekday &gt; Base.
/// </summary>
public class PricingRule : BaseEntity
{
    public Guid RoomTypeId { get; set; }
    public PricingRuleType RuleType { get; set; }

    /// <summary>Set only when RuleType == Weekday/Weekend.</summary>
    public DayOfWeek? DayOfWeek { get; set; }

    /// <summary>Set only when RuleType == Seasonal/Holiday/CustomDate.</summary>
    public DateOnly? DateRangeStart { get; set; }
    public DateOnly? DateRangeEnd { get; set; }

    public decimal PricePerNight { get; set; }

    public RoomType RoomType { get; set; } = null!;
}
