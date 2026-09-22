using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Users.Interfaces;

/// <summary>
/// Persistence boundary for the business profile associated with an identity account.
///
/// Authentication and security operations do not belong in this repository.
/// Repositories track changes; IUnitOfWork controls persistence boundaries.
/// </summary>
public interface IUserAccountRepository
{
    Task<UserAccount?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default);

    Task AddAsync(
        UserAccount account,
        CancellationToken ct = default);

    /// <summary>
    /// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): ids of accounts whose
    /// delay-window deletion request has matured (<c>DeletionScheduledFor &lt;= asOfUtc</c>)
    /// and has not already executed. Ids only, not tracked entities — the sweep re-loads and
    /// mutates each one inside its own scope/transaction via <see cref="GetByIdAsync"/>, the
    /// same pattern <c>AuditLogRetentionHostedService</c> uses for its own batched sweep.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetDueForDeletionAsync(
        DateTime asOfUtc,
        int batchSize,
        CancellationToken ct = default);
}