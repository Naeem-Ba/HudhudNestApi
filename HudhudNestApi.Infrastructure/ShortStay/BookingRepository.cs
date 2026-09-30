using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.ShortStay;

public sealed class BookingRepository : IBookingRepository
{
    private readonly AppDbContext _db;
    public BookingRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(Booking booking, CancellationToken ct = default)
        => await _db.ShortStayBookings.AddAsync(booking, ct);

    public async Task<Booking?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ShortStayBookings.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<Booking?> GetByIdWithListingAsync(Guid id, CancellationToken ct = default)
        => await _db.ShortStayBookings
            .Include(b => b.Unit)
                .ThenInclude(u => u!.RoomType)
                    .ThenInclude(rt => rt.ShortStayListing)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    // Half-open interval overlap: existing.CheckIn < newCheckOut AND newCheckIn < existing.CheckOut.
    // This is the application-level check run inside the advisory-lock-guarded transaction; the
    // DB-level EXCLUDE constraint (AddShortStayAvailability migration) is the unconditional
    // backstop that holds even if this check is somehow bypassed.
    public async Task<bool> HasOverlappingReservationAsync(
        Guid unitId, DateOnly checkIn, DateOnly checkOut, CancellationToken ct = default)
        => await _db.UnitBookingRanges.AnyAsync(r =>
            r.UnitId == unitId
            && (r.Status == UnitRangeStatus.Reserved || r.Status == UnitRangeStatus.CheckedIn)
            && r.CheckIn < checkOut && checkIn < r.CheckOut, ct);

    public async Task AddBookingRangeAsync(UnitBookingRange range, CancellationToken ct = default)
        => await _db.UnitBookingRanges.AddAsync(range, ct);

    public async Task MarkRangeReservedAsync(Guid bookingId, CancellationToken ct = default)
    {
        var range = await _db.UnitBookingRanges.FirstOrDefaultAsync(r => r.BookingId == bookingId, ct);
        if (range is not null)
            range.Status = UnitRangeStatus.Reserved;
    }

    public async Task MarkRangeCheckedInAsync(Guid bookingId, CancellationToken ct = default)
    {
        var range = await _db.UnitBookingRanges.FirstOrDefaultAsync(r => r.BookingId == bookingId, ct);
        if (range is not null)
            range.Status = UnitRangeStatus.CheckedIn;
    }

    public async Task ReleaseRangeAsync(Guid bookingId, CancellationToken ct = default)
    {
        var range = await _db.UnitBookingRanges.FirstOrDefaultAsync(r => r.BookingId == bookingId, ct);
        if (range is not null)
            range.Status = UnitRangeStatus.Released; // frees the dates immediately (excluded from the EXCLUDE constraint's WHERE clause)
    }

    public async Task<IReadOnlyList<Booking>> GetOverlappingPendingBookingsAsync(
        Guid unitId, DateOnly checkIn, DateOnly checkOut, Guid excludingBookingId, CancellationToken ct = default)
        => await _db.ShortStayBookings
            .Where(b => b.UnitId == unitId
                && b.Status == BookingStatus.Pending
                && b.Id != excludingBookingId
                && b.CheckIn < checkOut && checkIn < b.CheckOut)
            .ToListAsync(ct);

    // Both lists read the listing with its soft-delete filter off: a booking outlives its listing (dates,
    // money, history), and with the filter on, the required Unit -> RoomType -> ShortStayListing chain is an
    // inner join, so every booking of a deleted listing vanished from both the guest's and the host's list
    // (ShortStayBookingsOfDeletedListingTests). The booking's own soft delete is still honoured explicitly.
    public async Task<IReadOnlyList<Booking>> GetByGuestIdAsync(Guid guestId, CancellationToken ct = default)
        => await _db.ShortStayBookings
            .IgnoreQueryFilters()
            .Include(b => b.Unit)
                .ThenInclude(u => u!.RoomType)
                    .ThenInclude(rt => rt.ShortStayListing)
            .Where(b => !b.IsDeleted && b.GuestId == guestId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Booking>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default)
        => await _db.ShortStayBookings
            .IgnoreQueryFilters()
            .Include(b => b.Unit)
                .ThenInclude(u => u!.RoomType)
                    .ThenInclude(rt => rt.ShortStayListing)
            .Where(b => !b.IsDeleted && b.Unit!.RoomType.ShortStayListing.OwnerId == hostId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> HasCompletedBookingAsync(Guid bookingId, Guid guestId, CancellationToken ct = default)
        => await _db.ShortStayBookings.AnyAsync(
            b => b.Id == bookingId && b.GuestId == guestId && b.Status == BookingStatus.Completed, ct);

    public async Task<bool> HasActiveBookingsForListingAsync(Guid listingId, CancellationToken ct = default)
        => await _db.ShortStayBookings.AnyAsync(b =>
            b.Unit!.RoomType.ShortStayListingId == listingId &&
            (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Approved ||
             b.Status == BookingStatus.DepositPaid || b.Status == BookingStatus.Confirmed ||
             b.Status == BookingStatus.CheckedIn || b.Status == BookingStatus.CheckedOut), ct);

    public async Task<bool> HasReviewAsync(Guid bookingId, CancellationToken ct = default)
        => await _db.ShortStayReviews.AnyAsync(r => r.BookingId == bookingId, ct);

    public async Task<IReadOnlyList<UnitBookingRange>> GetRangesForUnitAsync(
        Guid unitId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => await _db.UnitBookingRanges
            .Where(r => r.UnitId == unitId
                && r.Status != UnitRangeStatus.Released
                && r.CheckIn < to && from < r.CheckOut)
            .OrderBy(r => r.CheckIn)
            .ToListAsync(ct);
}
