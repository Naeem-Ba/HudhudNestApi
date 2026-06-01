using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using WohnungenApi.Application.Common.Interfaces;
using WohnungenApi.Infrastructure.Persistence;

namespace WohnungenApi.Infrastructure.Persistence;

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

    public async Task BeginTransactionAsync()
        => _transaction = await _db.Database.BeginTransactionAsync();

    public async Task CommitTransactionAsync()
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction.");
        await _transaction.CommitAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync()
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public void Dispose() => _transaction?.Dispose();
}