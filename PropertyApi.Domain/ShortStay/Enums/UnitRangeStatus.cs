namespace PropertyApi.Domain.ShortStay.Enums;

/// <summary>
/// Status of a date range held against an AccommodationUnit. Only Reserved/CheckedIn
/// ranges are covered by the DB-level EXCLUDE constraint that guarantees no double-booking
/// (see the AddShortStayAvailability migration); Pending/Blocked ranges may legitimately
/// overlap — several pending requests can compete for the same dates, and a host block is
/// informational only.
/// </summary>
public enum UnitRangeStatus
{
    Pending = 0,
    Reserved = 1,
    CheckedIn = 2,
    Blocked = 3,
    Released = 4,
}
