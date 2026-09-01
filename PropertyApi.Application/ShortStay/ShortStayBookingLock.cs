namespace PropertyApi.Application.ShortStay;

/// <summary>
/// Derives a PostgreSQL advisory-lock key scoped to one AccommodationUnit, mirroring
/// ListingQuotaLock's approach (RELEASE-BLOCKERS-AR.md B-10) for the same reason: the
/// "check no overlapping reservation, then insert one" window in CreateBookingCommandHandler
/// is a check-then-write race across rows that a single-row unique constraint cannot close by
/// itself. The lock is the first line of defense; the EXCLUDE constraint added in the
/// AddShortStayAvailability migration is the second, DB-enforced one that still holds even if
/// a caller somehow bypasses the lock.
/// </summary>
public static class ShortStayBookingLock
{
    private const long UnitDomainTag = 0x5353_554E_4954_4C31; // "SSUNITL1" (ASCII bytes)

    public static long ForUnit(Guid unitId) => Fold(unitId) ^ UnitDomainTag;

    private static long Fold(Guid id) => BitConverter.ToInt64(id.ToByteArray(), 0);
}
