using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// SaveChanges belongs to Unit of Work — NOT to individual repositories.
// This prevents the anti-pattern of calling repo.SaveChangesAsync()
// mid-operation before all changes are complete.

namespace HudhudNestApi.Application.Common.Interfaces;

public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Valuation remediation H1/H2 — attempts to save, returning <c>false</c> instead of
    /// throwing when the ONLY reason the save failed is an optimistic-concurrency conflict
    /// (an xmin-mapped row was changed by another writer since it was loaded). Any other
    /// failure (validation, a genuine database error, connection loss) still throws normally
    /// — this is not a general-purpose "swallow errors" helper.
    ///
    /// Exists for "best-effort" side transitions that must never invalidate work already
    /// committed earlier in the same request: e.g. SubmitOfficeResponseCommandHandler saves
    /// the office's own response first (must succeed or the whole request fails), then
    /// separately attempts to complete the parent ValuationInquiry only if every invitation is
    /// now resolved — if a concurrent sibling response or the SLA sweep already won that
    /// second, unrelated race, the office's own already-saved response must not be rolled back
    /// or reported as failed because of it.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Valuation remediation H1/M2 — saves a batch that may contain several independent
    /// entities (e.g. ValuationInquiryExpiryHostedService's sweep, expiring up to
    /// MaxItemsPerPhase rows in one pass), tolerating the case where ONE of them lost an
    /// optimistic-concurrency race to a different writer (a concurrent
    /// SubmitOfficeResponseCommandHandler request completing/responding to that same row)
    /// without rolling back every OTHER row's legitimate transition in the same batch.
    /// Conflicted entities are detached and retried out of the batch; the caller gets back the
    /// exact entity instances that were dropped (by reference — safe to match against the same
    /// in-memory list it built the batch from) purely so it can log/skip them, since those
    /// rows were already resolved by someone else and must not also be reported (e.g.
    /// notified) as if this sweep had expired them.
    /// </summary>
    Task<IReadOnlyList<object>> SaveChangesDroppingConcurrencyConflictsAsync(CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(
    CancellationToken ct = default);

    Task CommitTransactionAsync(
        CancellationToken ct = default);

    Task RollbackTransactionAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Acquires a PostgreSQL transaction-scoped advisory lock (<c>pg_advisory_xact_lock</c>),
    /// blocking until it is free. Requires an active transaction (started via
    /// <see cref="BeginTransactionAsync"/>) — the lock is released automatically when that
    /// transaction commits or rolls back, so a caller can never leak it by forgetting to
    /// release explicitly, even on an unhandled exception.
    ///
    /// Used to close read-then-write races on a check that spans multiple rows and cannot be
    /// expressed as a single unique constraint (RELEASE-BLOCKERS-AR.md B-10): two concurrent
    /// callers with the same key serialize instead of both reading the same "count so far"
    /// and both proceeding.
    /// </summary>
    Task AcquireAdvisoryLockAsync(long key, CancellationToken ct = default);
}

