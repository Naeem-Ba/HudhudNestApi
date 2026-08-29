using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.ShortStay.Enums;

namespace PropertyApi.Domain.ShortStay.Entities;

/// <summary>
/// Single source of truth for a unit's occupied/blocked dates. Reserved/CheckedIn ranges
/// are guaranteed non-overlapping per unit by a DB-level EXCLUDE constraint (see the
/// AddShortStayAvailability migration) — that is the real double-booking guard, not just
/// application logic. Pending ranges may legitimately overlap each other (several guests
/// can request the same dates; the host picks one). Blocked ranges have no BookingId and
/// represent a host-side manual block (maintenance, personal use, etc.).
/// </summary>
public class UnitBookingRange : BaseEntity
{
    public Guid UnitId { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public UnitRangeStatus Status { get; set; }

    /// <summary>Null for a host-side manual Blocked range.</summary>
    public Guid? BookingId { get; set; }

    public AccommodationUnit Unit { get; set; } = null!;
    public Booking? Booking { get; set; }
}
