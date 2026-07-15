using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Identity.Services;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Auth.Repositories;

public sealed class RefreshTokenRepository
    : IRefreshTokenRepository
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwtOptions;

    private readonly ILogger<RefreshTokenRepository>
        _logger;

    public RefreshTokenRepository(
        AppDbContext db,
        IOptions<JwtOptions> jwtOptions,
        ILogger<RefreshTokenRepository> logger)
    {
        _db = db;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task AddAsync(
        Guid userId,
        string refreshToken,
        string? createdByIp,
        CancellationToken ct = default)
    {
        var now =
            DateTime.UtcNow;

        var newTokenHash =
            HashToken(refreshToken);

        var revokedOldTokens =
            await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(
                    token =>
                        token.UserId == userId &&
                        !token.IsRevoked &&
                        token.ExpiresAt > now)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                token =>
                                    token.IsRevoked,
                                true)
                            .SetProperty(
                                token =>
                                    token.RevokedAt,
                                now)
                            .SetProperty(
                                token =>
                                    token.RevokedByIp,
                                createdByIp)
                            .SetProperty(
                                token =>
                                    token.ReplacedByTokenHash,
                                newTokenHash),
                    ct);

        if (revokedOldTokens > 0)
        {
            _logger.LogInformation(
                "Revoked {Count} active refresh token(s) before issuing a new token for user {UserId}.",
                revokedOldTokens,
                userId);
        }

        _db.RefreshTokens.Add(
            new RefreshToken
            {
                TokenHash =
                    newTokenHash,

                UserId =
                    userId,

                ExpiresAt =
                    now.AddDays(
                        _jwtOptions.RefreshTokenDays),

                CreatedByIp =
                    createdByIp,

                LastUsedAt =
                    null
            });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<RefreshTokenRecord?>
        GetByRefreshTokenAsync(
            string refreshToken,
            CancellationToken ct = default)
    {
        var hash =
            HashToken(refreshToken);

        var now =
            DateTime.UtcNow;

        /*
         * Do not load the Identity user entity.
         *
         * The Application layer only requires token metadata and UserId.
         * Identity state is resolved through Application identity
         * capability contracts.
         */
        var token =
            await _db.RefreshTokens
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(
                    candidate =>
                        candidate.TokenHash == hash)
                .Select(
                    candidate =>
                        new RefreshTokenRecord(
                            candidate.Id,
                            candidate.UserId,
                            candidate.ExpiresAt,
                            candidate.IsRevoked))
                .SingleOrDefaultAsync(ct);

        if (token is null)
        {
            return null;
        }

        if (!token.IsRevoked &&
            token.ExpiresAt > now)
        {
            await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(
                    candidate =>
                        candidate.Id == token.Id)
                .ExecuteUpdateAsync(
                    setters =>
                        setters.SetProperty(
                            candidate =>
                                candidate.LastUsedAt,
                            now),
                    ct);

            _logger.LogInformation(
                "Refresh token last-used timestamp updated for user {UserId}.",
                token.UserId);
        }

        return token;
    }

    public async Task<bool> RevokeIfActiveAsync(
        Guid tokenId,
        DateTime now,
        string? revokedByIp,
        string? replacedByRefreshToken,
        CancellationToken ct = default)
    {
        var replacementHash =
            string.IsNullOrWhiteSpace(
                replacedByRefreshToken)
                ? null
                : HashToken(
                    replacedByRefreshToken);

        var affectedRows =
            await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(
                    token =>
                        token.Id == tokenId &&
                        !token.IsRevoked &&
                        token.ExpiresAt > now)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                token =>
                                    token.IsRevoked,
                                true)
                            .SetProperty(
                                token =>
                                    token.RevokedAt,
                                now)
                            .SetProperty(
                                token =>
                                    token.RevokedByIp,
                                revokedByIp)
                            .SetProperty(
                                token =>
                                    token.ReplacedByTokenHash,
                                replacementHash),
                    ct);

        if (affectedRows == 1)
        {
            _logger.LogInformation(
                "Refresh token {TokenId} was revoked successfully.",
                tokenId);
        }

        return affectedRows == 1;
    }

    public async Task RevokeActiveTokensForUserAsync(
        Guid userId,
        DateTime now,
        string? revokedByIp,
        CancellationToken ct = default)
    {
        var affectedRows =
            await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(
                    token =>
                        token.UserId == userId &&
                        !token.IsRevoked &&
                        token.ExpiresAt > now)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                token =>
                                    token.IsRevoked,
                                true)
                            .SetProperty(
                                token =>
                                    token.RevokedAt,
                                now)
                            .SetProperty(
                                token =>
                                    token.RevokedByIp,
                                revokedByIp),
                    ct);

        if (affectedRows > 0)
        {
            _logger.LogInformation(
                "Revoked {Count} active refresh token(s) for user {UserId}.",
                affectedRows,
                userId);
        }
    }

    public async Task<bool> RevokeUserTokenAsync(
        Guid userId,
        string refreshToken,
        DateTime now,
        string? revokedByIp,
        CancellationToken ct = default)
    {
        var hash =
            HashToken(refreshToken);

        var affectedRows =
            await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(
                    token =>
                        token.TokenHash == hash &&
                        token.UserId == userId &&
                        !token.IsRevoked &&
                        token.ExpiresAt > now)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                token =>
                                    token.IsRevoked,
                                true)
                            .SetProperty(
                                token =>
                                    token.RevokedAt,
                                now)
                            .SetProperty(
                                token =>
                                    token.RevokedByIp,
                                revokedByIp),
                    ct);

        if (affectedRows == 1)
        {
            _logger.LogInformation(
                "Refresh token was revoked for user {UserId}.",
                userId);
        }

        return affectedRows == 1;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        await using var transaction =
            await _db.Database
                .BeginTransactionAsync(ct);

        try
        {
            var result =
                await action(ct);

            await transaction.CommitAsync(ct);

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);

            throw;
        }
    }

    private static string HashToken(
        string token)
    {
        var bytes =
            Encoding.UTF8.GetBytes(token);

        var hash =
            SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }
}
