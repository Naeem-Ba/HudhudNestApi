using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Interfaces;

/// <summary>
/// Repository interface for user read operations.
/// User management and password operations go through Application-level Identity abstractions,
/// implemented in Infrastructure.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetAllActiveAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

