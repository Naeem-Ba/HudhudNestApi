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
    bool IsRevoked,
    DateTime? RevokedAt = null,
    string? ReplacedByTokenHash = null);

/// <summary>
/// Values stored in <c>ReplacedByTokenHash</c> that are not the hash of a successor token. They tell a revoked
/// token that was legitimately ended (the user signed in on too many devices) apart from one that was
/// rotated away, whose replay is a theft signal.
/// </summary>
public static class RefreshTokenMarkers
{
    /// <summary>Ended because a newer sign-in pushed this session out of the allowed number of devices.</summary>
    public const string Superseded = "superseded";
}

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Stores the refresh token of a NEW sign-in. Sessions beyond the allowed number of devices (the oldest
    /// ones) are ended and marked <see cref="RefreshTokenMarkers.Superseded"/>.
    /// </summary>
    Task AddAsync(
        Guid userId,
        string refreshToken,
        string? createdByIp,
        CancellationToken ct = default);

    /// <summary>
    /// Stores the refresh token that replaces one being rotated. Unlike <see cref="AddAsync"/> it ends no other
    /// session: a refresh on the phone must not sign the website out.
    /// </summary>
    Task AddRotatedAsync(
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