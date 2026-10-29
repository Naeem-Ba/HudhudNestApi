namespace HudhudNestApi.Domain.ShortStay;

/// <summary>
/// The owner's rules for short-stay bookings (QA Phase 9 fix package, 2026-10-18). Kept in one place so the
/// booking handlers, the expiry job and the tests read the same numbers.
/// </summary>
public static class ShortStayBookingPolicy
{
    /// <summary>An instant booking may only start within this many days from today ("one month at most").</summary>
    public const int InstantMaxAdvanceDays = 30;

    /// <summary>An instant booking may cover at most this many nights.</summary>
    public const int InstantMaxNights = 30;

    /// <summary>
    /// How many unfinished instant bookings one guest may hold at a time. Instant bookings reserve the unit
    /// without the host's say, so without a cap one account could block every instant-booking listing.
    /// </summary>
    public const int MaxActiveInstantBookingsPerGuest = 3;

    /// <summary>A request the host has not answered within this time expires.</summary>
    public static readonly TimeSpan PendingResponseWindow = TimeSpan.FromDays(3);

    /// <summary>An approved booking that is not confirmed within this time after approval expires.</summary>
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromDays(3);

    /// <summary>
    /// Nothing may stay unconfirmed longer than this after the booking was made ("three days, a week at the most").
    /// </summary>
    public static readonly TimeSpan MaxOpenWindow = TimeSpan.FromDays(7);

    /// <summary>A host may keep at most this many manual blocks per unit.</summary>
    public const int MaxBlocksPerUnit = 100;

    /// <summary>A single manual block may cover at most this many nights.</summary>
    public const int MaxBlockNights = 366;

    /// <summary>A manual block may start at most this many days from today.</summary>
    public const int MaxBlockAdvanceDays = 730;
}
