using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Interfaces;

/// <summary>
/// Repository interface for User read operations.
/// Write operations go through Identity's UserManager&lt;User&gt;.
///
/// CRITICAL BUG FIX: Was declared as
/// "internal class IUserRepository {}" — a CLASS named as an interface.
/// This violates the Interface Segregation principle and would never compile
/// as an injectable interface.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
