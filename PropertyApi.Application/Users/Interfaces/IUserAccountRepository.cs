using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Interfaces;

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
}