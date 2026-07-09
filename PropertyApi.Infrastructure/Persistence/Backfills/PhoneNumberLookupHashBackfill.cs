using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Persistence.Backfills;

public sealed record PhoneNumberLookupHashBackfillResult(
    int UsersWithPhone,
    int MissingBefore,
    int Updated,
    int MissingAfter);

public sealed class PhoneNumberLookupHashBackfill
{
    private const int BatchSize = 250;

    private readonly AppDbContext _db;
    private readonly IPhoneNumberLookupHasher _phoneLookupHasher;
    private readonly ILogger<PhoneNumberLookupHashBackfill> _logger;

    public PhoneNumberLookupHashBackfill(
        AppDbContext db,
        IPhoneNumberLookupHasher phoneLookupHasher,
        ILogger<PhoneNumberLookupHashBackfill> logger)
    {
        _db = db;
        _phoneLookupHasher = phoneLookupHasher;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupHashBackfillResult> RunAsync(
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting PhoneNumberLookupHash backfill.");

        /*
         * This is a historical data migration.
         *
         * Soft-deleted users must also be inspected so that every
         * existing encrypted phone number has a corresponding
         * deterministic lookup hash.
         */
        var totalUsersWithPhone =
            await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(
                    user =>
                        user.PhoneNumber != null,
                    ct);

        var missingBefore =
            await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(
                    user =>
                        user.PhoneNumber != null &&
                        user.PhoneNumberLookupHash == null,
                    ct);

        _logger.LogInformation(
            "Phone lookup backfill state before execution: " +
            "UsersWithPhone={UsersWithPhone}, MissingLookupHash={MissingLookupHash}",
            totalUsersWithPhone,
            missingBefore);

        if (missingBefore == 0)
        {
            _logger.LogInformation(
                "No users require PhoneNumberLookupHash backfill.");

            return new PhoneNumberLookupHashBackfillResult(
                UsersWithPhone: totalUsersWithPhone,
                MissingBefore: 0,
                Updated: 0,
                MissingAfter: 0);
        }

        /*
         * Load hashes which already exist.
         *
         * These participate in the preflight conflict check.
         */
        var existingHashes =
            await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(
                    user =>
                        user.PhoneNumberLookupHash != null)
                .Select(
                    user =>
                        user.PhoneNumberLookupHash!)
                .ToListAsync(ct);

        var knownHashes =
            existingHashes.ToHashSet(
                StringComparer.Ordinal);

        /*
         * Read legacy users through EF Core.
         *
         * The configured value converter materializes PhoneNumber
         * into its CLR/model value before the hasher receives it.
         */
        var candidates =
            await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(
                    user =>
                        user.PhoneNumber != null &&
                        user.PhoneNumberLookupHash == null)
                .OrderBy(user => user.Id)
                .Select(
                    user => new
                    {
                        user.Id,
                        user.PhoneNumber
                    })
                .ToListAsync(ct);

        var computedHashes =
            new Dictionary<Guid, string>(
                candidates.Count);

        /*
         * Preflight all candidates before writing anything.
         *
         * This detects:
         * - blank historical phone values
         * - duplicate phone identities among candidates
         * - conflicts with already populated hashes
         *
         * Phone numbers themselves are never logged.
         */
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(
                    candidate.PhoneNumber))
            {
                throw new InvalidOperationException(
                    $"User '{candidate.Id}' has an empty phone number " +
                    "and cannot be included in the phone lookup backfill.");
            }

            var hash =
                _phoneLookupHasher.Compute(
                    candidate.PhoneNumber);

            if (!knownHashes.Add(hash))
            {
                throw new InvalidOperationException(
                    "Duplicate or conflicting phone lookup identity " +
                    $"detected for user '{candidate.Id}'. " +
                    "Backfill stopped before modifying data.");
            }

            computedHashes.Add(
                candidate.Id,
                hash);
        }

        _logger.LogInformation(
            "Phone lookup preflight completed successfully. Candidates={CandidateCount}",
            computedHashes.Count);

        var orderedEntries =
            computedHashes
                .OrderBy(entry => entry.Key)
                .ToArray();

        var updatedCount = 0;

        /*
         * Persist in bounded batches.
         *
         * Rows which already contain a lookup hash are excluded,
         * making repeated execution idempotent.
         */
        for (
            var offset = 0;
            offset < orderedEntries.Length;
            offset += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch =
                orderedEntries
                    .Skip(offset)
                    .Take(BatchSize)
                    .ToArray();

            var batchIds =
                batch
                    .Select(entry => entry.Key)
                    .ToArray();

            var users =
                await _db.Users
                    .IgnoreQueryFilters()
                    .Where(
                        user =>
                            Enumerable.Contains(batchIds, user.Id) &&
                            user.PhoneNumberLookupHash == null)
                    .ToListAsync(ct);

            foreach (var user in users)
            {
                if (!computedHashes.TryGetValue(
                        user.Id,
                        out var hash))
                {
                    throw new InvalidOperationException(
                        "Computed phone lookup hash was not found " +
                        $"for user '{user.Id}'.");
                }

                user.PhoneNumberLookupHash =
                    hash;
            }

            await _db.SaveChangesAsync(ct);

            updatedCount +=
                users.Count;

            _logger.LogInformation(
                "Phone lookup backfill progress: Updated={UpdatedCount}/{CandidateCount}",
                updatedCount,
                computedHashes.Count);

            _db.ChangeTracker.Clear();
        }

        var missingAfter =
            await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(
                    user =>
                        user.PhoneNumber != null &&
                        user.PhoneNumberLookupHash == null,
                    ct);

        var usersWithPhoneAndHash =
            await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(
                    user =>
                        user.PhoneNumber != null &&
                        user.PhoneNumberLookupHash != null,
                    ct);

        if (missingAfter != 0)
        {
            throw new InvalidOperationException(
                "Phone lookup backfill verification failed. " +
                $"{missingAfter} user(s) still have a phone number " +
                "without PhoneNumberLookupHash.");
        }

        _logger.LogInformation(
            "PhoneNumberLookupHash backfill completed successfully. " +
            "Updated={UpdatedCount}, " +
            "UsersWithPhoneAndHash={UsersWithPhoneAndHash}, " +
            "MissingAfter={MissingAfter}",
            updatedCount,
            usersWithPhoneAndHash,
            missingAfter);

        return new PhoneNumberLookupHashBackfillResult(
            UsersWithPhone: totalUsersWithPhone,
            MissingBefore: missingBefore,
            Updated: updatedCount,
            MissingAfter: missingAfter);
    }
}