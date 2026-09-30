namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// Persistence-neutral refresh-token state.
///
/// The repository exposes only token metadata and the owning identity Id.
/// It deliberately does not expose a User or ApplicationUser entity.
/// </summary>
public sealed record RefreshTokenRecord(
    Guid Id,
    Guid UserId,
    DateTime ExpiresAt,
    bool IsRevoked);

public interface IRefreshTokenRepository
{
    Task AddAsync(
        Guid userId,
        string refreshToken,
        string? createdByIp,
        CancellationToken ct = default);

    Task<RefreshTokenRecord?> GetByRefreshTokenAsync(
        string refreshToken,
        CancellationToken ct = default);

    Task<bool> RevokeIfActiveAsync(
        Guid tokenId,
        DateTime now,
        string? revokedByIp,
        string? replacedByRefreshToken,
        CancellationToken ct = default);

    Task RevokeActiveTokensForUserAsync(
        Guid userId,
        DateTime now,
        string? revokedByIp,
        CancellationToken ct = default);

    Task<bool> RevokeUserTokenAsync(
        Guid userId,
        string refreshToken,
        DateTime now,
        string? revokedByIp,
        CancellationToken ct = default);

    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default);
}