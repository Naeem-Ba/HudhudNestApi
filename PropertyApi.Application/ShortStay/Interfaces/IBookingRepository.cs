using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Interfaces;

public interface IBookingRepository
{
    Task AddAsync(Booking booking, CancellationToken ct = default);
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>True when the unit has any Reserved/CheckedIn range overlapping [checkIn, checkOut).
    /// This is a defense-in-depth check done inside the advisory-lock-protected transaction —
    /// the DB-level EXCLUDE constraint (see AddShortStayAvailability migration) is the real
    /// guarantee and will still reject a race that slips past this check.</summary>
    Task<bool> HasOverlappingReservationAsync(
        Guid unitId,
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken ct = default);

    Task AddBookingRangeAsync(UnitBookingRange range, CancellationToken ct = default);

    /// <summary>Moves the booking's date range from Pending to Reserved (called on Approve/instant-Confirm).</summary>
    Task MarkRangeReservedAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>Moves the booking's date range from Reserved to CheckedIn (called on CheckIn).</summary>
    Task MarkRangeCheckedInAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>Removes the booking's held date range entirely (called on Reject/Cancel/Expire —
    /// frees the dates immediately for other guests).</summary>
    Task ReleaseRangeAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>Loads a booking together with its Unit -&gt; RoomType -&gt; ShortStayListing chain,
    /// needed to resolve host ownership and listing title without extra round-trips.</summary>
    Task<Booking?> GetByIdWithListingAsync(Guid id, CancellationToken ct = default);

    /// <summary>Rejects every other Pending booking for the same unit whose dates overlap the
    /// just-approved booking (spec Scenario D: approving one request auto-clears the rest).</summary>
    Task<IReadOnlyList<Booking>> GetOverlappingPendingBookingsAsync(
        Guid unitId,
        DateOnly checkIn,
        DateOnly checkOut,
        Guid excludingBookingId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Booking>> GetByGuestIdAsync(Guid guestId, CancellationToken ct = default);

    /// <summary>Bookings for every unit owned by this host, across all their listings.</summary>
    Task<IReadOnlyList<Booking>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default);

    Task<bool> HasCompletedBookingAsync(Guid bookingId, Guid guestId, CancellationToken ct = default);
    Task<bool> HasReviewAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>Every held/blocked range for a unit that overlaps [from, to) — the calendar view.</summary>
    Task<IReadOnlyList<UnitBookingRange>> GetRangesForUnitAsync(
        Guid unitId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
