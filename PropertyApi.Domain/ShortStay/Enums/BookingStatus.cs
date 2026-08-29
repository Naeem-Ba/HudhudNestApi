namespace PropertyApi.Domain.ShortStay.Enums;

/// <summary>
/// Booking lifecycle. Request-mode bookings start at Pending; Instant-mode bookings skip
/// straight to Confirmed (or Approved first, if the listing requires a deposit).
/// Allowed forward path:
///   Pending -&gt; Approved -&gt; DepositPaid -&gt; Confirmed -&gt; CheckedIn -&gt; CheckedOut -&gt; Completed
/// Terminal alternates reachable from an in-flight state:
///   Rejected (from Pending/Approved only), Cancelled (any state before CheckedIn),
///   Expired (Pending only, via background job), NoShow (Confirmed only, via background job).
/// No transition may skip forward more than one step or move backward — enforced in Booking.
/// </summary>
public enum BookingStatus
{
    Pending = 0,
    Approved = 1,
    DepositPaid = 2,
    Confirmed = 3,
    CheckedIn = 4,
    CheckedOut = 5,
    Completed = 6,
    Rejected = 7,
    Cancelled = 8,
    Expired = 9,
    NoShow = 10,
}
