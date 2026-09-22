using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

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

    // BUG FIX: this query was `.AsNoTracking()`, which directly contradicted this class's own
    // documented contract above ("The repository tracks changes only") — every caller that
    // loads an account here (UpdateUserCommandHandler, DeleteUserCommandHandler,
    // UploadUserAvatarCommandHandler, SelectPlanCommandHandler, RateUserCommandHandler,
    // AddServiceReviewCommandHandler, AdminSubscriptionService, ...) mutates the returned
    // entity in place and then calls IUnitOfWork.SaveChangesAsync(), expecting EF Core's change
    // tracker to pick up the modification. With AsNoTracking, the entity is detached: no
    // ChangeTracker entry is ever created, AppDbContext.SaveChangesAsync's
    // `ChangeTracker.Entries()` sweep never sees it, and every one of those writes silently
    // no-ops — confirmed empirically (see UserProfilePersistenceTests, a real HTTP round trip
    // through PUT and DELETE /api/Users/me against a real Postgres database) for both profile
    // updates and the GDPR account-deletion anonymization this repository now also backs.
    public async Task<UserAccount?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default)
    {
        return await _db.UserAccounts
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

    public async Task<IReadOnlyList<Guid>> GetDueForDeletionAsync(
        DateTime asOfUtc,
        int batchSize,
        CancellationToken ct = default)
    {
        return await _db.UserAccounts
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.DeletionScheduledFor != null && a.DeletionScheduledFor <= asOfUtc)
            .OrderBy(a => a.DeletionScheduledFor)
            .Take(batchSize)
            .Select(a => a.Id)
            .ToListAsync(ct);
    }
}