using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Persistence;

/// <summary>
/// Unit of Work — the single point for committing all changes.
/// Repositories only track changes; UoW decides when to flush them.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(AppDbContext db) => _db = db;

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);

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
