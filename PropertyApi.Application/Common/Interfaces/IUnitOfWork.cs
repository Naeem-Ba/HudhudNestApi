using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// SaveChanges belongs to Unit of Work — NOT to individual repositories.
// This prevents the anti-pattern of calling repo.SaveChangesAsync()
// mid-operation before all changes are complete.

namespace PropertyApi.Application.Common.Interfaces;

public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
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

