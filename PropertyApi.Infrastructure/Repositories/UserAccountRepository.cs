using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

/// <summary>
/// Repository for business profiles stored in UserAccounts.
///
/// The repository tracks changes only.
/// Transaction and SaveChanges boundaries are controlled by IUnitOfWork.
/// </summary>
public sealed class UserAccountRepository : IUserAccountRepository
{
    private readonly AppDbContext _db;

    public UserAccountRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserAccount?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await _db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                account => account.Id == id,
                ct);
    }

    public async Task AddAsync(
        UserAccount account,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await _db.UserAccounts.AddAsync(account, ct);
    }
}