using HudhudNestApi.Domain.Common.Entities;

namespace HudhudNestApi.Domain.ShortStay.Entities;

/// <summary>
/// Optional grouping/pricing layer between a listing and its bookable units. A
/// single-property listing (chalet, villa...) gets exactly one auto-created RoomType with
/// one Unit at creation time, so booking logic never special-cases "no room types" — a
/// hotel listing simply has more than one RoomType, each with its own Units.
/// </summary>
public class RoomType : BaseEntity
{
    public Guid ShortStayListingId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Nightly base price used when no PricingRule applies. Overridable per-rule.</summary>
    public decimal BasePricePerNight { get; set; }

    /// <summary>Null = inherit the listing's Capacity.</summary>
    public int? Capacity { get; set; }

    public bool IsActive { get; set; } = true;

    public ShortStayListing ShortStayListing { get; set; } = null!;
    public ICollection<AccommodationUnit> Units { get; set; } = new List<AccommodationUnit>();
    public ICollection<PricingRule> PricingRules { get; set; } = new List<PricingRule>();
    public ICollection<MinimumStayRule> MinimumStayRules { get; set; } = new List<MinimumStayRule>();
}
