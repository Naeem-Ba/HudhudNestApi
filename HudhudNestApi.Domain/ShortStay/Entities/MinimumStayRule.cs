using HudhudNestApi.Domain.Common.Entities;

namespace HudhudNestApi.Domain.ShortStay.Entities;

/// <summary>
/// Minimum-nights requirement for a RoomType. A null date range means the "default" rule;
/// a rule with a date range overrides the default for nights that fall inside it. When
/// multiple date-ranged rules match the same date, the highest MinimumNights wins (safest
/// interpretation for the host) — enforced in PricingCalculationService.
/// </summary>
public class MinimumStayRule : BaseEntity
{
    public Guid RoomTypeId { get; set; }
    public int MinimumNights { get; set; }
    public DateOnly? DateRangeStart { get; set; }
    public DateOnly? DateRangeEnd { get; set; }

    public RoomType RoomType { get; set; } = null!;
}
