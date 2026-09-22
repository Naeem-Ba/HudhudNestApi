using HudhudNestApi.Domain.Common.Entities;

namespace HudhudNestApi.Domain.ShortStay.Entities;

/// <summary>
/// The actual bookable inventory item. A Booking always targets a Unit — never a Listing
/// or a RoomType directly — so a single-property listing (1 RoomType, 1 Unit) and a hotel
/// (N RoomTypes, M Units each) go through identical booking/availability logic.
/// </summary>
public class AccommodationUnit : BaseEntity
{
    public Guid RoomTypeId { get; set; }

    /// <summary>Display label, e.g. "الوحدة الوحيدة" for a single property, or "غرفة 101" in a hotel.</summary>
    public string Label { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public RoomType RoomType { get; set; } = null!;
    public ICollection<UnitBookingRange> BookingRanges { get; set; } = new List<UnitBookingRange>();
}
