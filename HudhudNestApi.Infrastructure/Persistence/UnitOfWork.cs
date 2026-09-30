using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Persistence;

/// <summary>
/// Unit of Work — the single point for committing all changes.
/// Repositories only track changes; UoW decides when to flush them.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Valuation remediation M1 — named unique constraints whose violation is a known,
    /// expected "someone already did this" race rather than a database error, mapped to the
    /// existing ConflictException -> 409 pattern the rest of this codebase already uses for
    /// Domain-level state conflicts (see SubmitOfficeResponseCommandHandler's own
    /// DomainException -> ConflictException catch for the in-memory half of this same guard).
    /// Deliberately a narrow, explicit allow-list keyed by constraint name — NOT a blanket
    /// "every DbUpdateException is a 409": a connection failure, timeout, or any other
    /// genuine database error still surfaces unchanged (rethrown below), exactly as before
    /// this constant existed.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UniqueViolationConflictMessages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ValuationOfficeResponseConfiguration: at most one response per invitation, ever.
            // MarkResponded's own Sent-only Domain guard is the primary defense; this is the
            // database-level backstop for the read-then-write race two truly concurrent
            // submissions to the SAME invitation could otherwise hit.
            ["IX_ValuationOfficeResponses_InvitationId"] =
                "تم إرسال رد على هذه الدعوة بالفعل من قبل عملية أخرى.",
        };

    private readonly AppDbContext _db;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(AppDbContext db) => _db = db;

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (TryGetKnownUniqueViolationMessage(ex, out var message))
        {
            throw new ConflictException(message);
        }
    }

    /// <summary>
    /// True only when <paramref name="ex"/> wraps a Postgres unique-violation (SQLSTATE 23505)
    /// against one of the specific, named constraints in <see cref="UniqueViolationConflictMessages"/>.
    /// Any other cause (a different constraint, a different SQLSTATE, a connection failure, a
    /// plain timeout) returns false and the original exception is rethrown untouched by the
    /// caller — those must never become a 409.
    /// </summary>
    private static bool TryGetKnownUniqueViolationMessage(DbUpdateException ex, out string message)
    {
        message = string.Empty;

        if (ex.InnerException is not PostgresException pg
            || pg.SqlState != PostgresErrorCodes.UniqueViolation
            || pg.ConstraintName is null)
        {
            return false;
        }

        return UniqueViolationConflictMessages.TryGetValue(pg.ConstraintName, out message!);
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The caller lost an optimistic-concurrency race on a best-effort side transition.
            // Detach the entries EF now considers conflicted so a later SaveChangesAsync in
            // the same scope (e.g. the next inquiry in ValuationSlaEnforcementService's batch)
            // is not blocked by them.
            foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    public async Task<IReadOnlyList<object>> SaveChangesDroppingConcurrencyConflictsAsync(CancellationToken ct = default)
    {
        var dropped = new List<object>();
        while (true)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
                return dropped;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Each retry only ever needs to drop the entries that JUST conflicted —
                // everything else already survived a prior SaveChangesAsync attempt inside
                // this same call (or never conflicted at all) and stays tracked for the next
                // attempt.
                foreach (var entry in ex.Entries)
                {
                    dropped.Add(entry.Entity);
                    entry.State = EntityState.Detached;
                }
            }
        }
    }

    public async Task BeginTransactionAsync(
        CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException(
                "A transaction is already active.");

        _transaction = await _db.Database
            .BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(
        CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException(
                "No active transaction.");

        try
        {
            await _transaction.CommitAsync(ct);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(
        CancellationToken ct = default)
    {
        if (_transaction is null)
            return;

        try
        {
            await _transaction.RollbackAsync(ct);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task AcquireAdvisoryLockAsync(long key, CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException(
                "AcquireAdvisoryLockAsync requires an active transaction — call BeginTransactionAsync first.");

        // Transaction-scoped (xact, not session-level): released automatically at commit or
        // rollback, unlike BackgroundJobLock's session-level pg_try_advisory_lock which a
        // hosted service releases explicitly because its unit of work is not a DB transaction.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({key})", ct);
    }

    public void Dispose() => _transaction?.Dispose();
}
