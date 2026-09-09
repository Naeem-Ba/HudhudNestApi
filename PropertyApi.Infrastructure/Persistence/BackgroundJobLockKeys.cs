namespace PropertyApi.Infrastructure.Persistence;

/// <summary>
/// Distinct Postgres advisory-lock keys, one per recurring background job that mutates
/// shared database state. Every deployed instance runs the same hosted services; without a
/// lock, two instances race the same rows in the same sweep (see the six-instance gap this
/// closes for four of the six hosted services in the codebase — B-6 in
/// RELEASE-BLOCKERS-AR.md).
///
/// Not every hosted service needs a key here. Deliberately excluded:
/// - ProductionStartupValidator: runs once at boot to validate its own instance's
///   configuration. Running on every instance is the point, not a race.
/// - SecurityAlertBackgroundService: drains an in-process Channel fed only by requests
///   handled on that same instance. There is nothing cross-instance to coordinate, and
///   locking it would starve alerts on every instance that does not hold the lock.
///
/// Values are arbitrary but must be stable and unique — changing one after deployment just
/// means in-flight locks under the old key are silently abandoned, not corrupted. The
/// yyyyMMddHHmm-shaped literals follow the same convention as the one existing advisory
/// lock in this codebase (DatabaseSeeder.cs).
/// </summary>
public static class BackgroundJobLockKeys
{
    public const long ListingExpiry = 20260825_1001;

    public const long SavedSearchMatch = 20260825_1002;

    public const long PhoneVerificationReminder = 20260825_1003;

    public const long OtpCleanup = 20260825_1004;

    public const long AuditLogRetention = 20260905_1005;

    public const long AccountDeletionSweep = 20260907_1006;

    public const long SocialPublicationDispatch = 20260908_1007;
}
