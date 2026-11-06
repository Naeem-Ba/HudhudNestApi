using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Identity.Services;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Integration.Tests.TestInfrastructure;

internal sealed class EfInMemoryRefreshTokenRepository
    : IRefreshTokenRepository
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwtOptions;

    public EfInMemoryRefreshTokenRepository(
        AppDbContext db,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task AddAsync(
        Guid userId,
        string refreshToken,
        string? createdByIp,
        CancellationToken ct = default)
    {
        await AddCoreAsync(
            userId,
            refreshToken,
            createdByIp,
            endOtherSessions: true,
            ct);
    }

    public async Task AddRotatedAsync(
        Guid userId,
        string refreshToken,
        string? createdByIp,
        CancellationToken ct = default)
    {
        await AddCoreAsync(
            userId,
            refreshToken,
            createdByIp,
            endOtherSessions: false,
            ct);
    }

    public async Task<RefreshTokenRecord?> GetByRefreshTokenAsync(
        string refreshToken,
        CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);
        var now = DateTime.UtcNow;

        var token = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, ct);

        if (token is null)
            return null;

        if (!token.IsRevoked && token.ExpiresAt > now)
        {
            token.LastUsedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        return new RefreshTokenRecord(
            token.Id,
            token.UserId,
            token.ExpiresAt,
            token.IsRevoked,
            token.RevokedAt,
            token.ReplacedByTokenHash);
    }

    public async Task<bool> RevokeIfActiveAsync(
        Guid tokenId,
        DateTime now,
        string? revokedByIp,
        string? replacedByRefreshToken,
        CancellationToken ct = default)
    {
        var token = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == tokenId &&
                !candidate.IsRevoked &&
                candidate.ExpiresAt > now,
                ct);

        if (token is null)
            return false;

        token.IsRevoked = true;
        token.RevokedAt = now;
        token.RevokedByIp = revokedByIp;
        token.ReplacedByTokenHash =
            string.IsNullOrWhiteSpace(replacedByRefreshToken)
                ? null
                : HashToken(replacedByRefreshToken);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RevokeActiveTokensForUserAsync(
        Guid userId,
        DateTime now,
        string? revokedByIp,
        CancellationToken ct = default)
    {
        var tokens = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token =>
                token.UserId == userId &&
                !token.IsRevoked &&
                token.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
            token.RevokedByIp = revokedByIp;
        }

        if (tokens.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> RevokeUserTokenAsync(
        Guid userId,
        string refreshToken,
        DateTime now,
        string? revokedByIp,
        CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);

        var token = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate =>
                candidate.TokenHash == hash &&
                candidate.UserId == userId &&
                !candidate.IsRevoked &&
                candidate.ExpiresAt > now,
                ct);

        if (token is null)
            return false;

        token.IsRevoked = true;
        token.RevokedAt = now;
        token.RevokedByIp = revokedByIp;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        return action(ct);
    }

    private async Task AddCoreAsync(
        Guid userId,
        string refreshToken,
        string? createdByIp,
        bool endOtherSessions,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token is required.", nameof(refreshToken));

        var now = DateTime.UtcNow;
        var newTokenHash = HashToken(refreshToken);

        if (endOtherSessions)
        {
            // Same rule as the real repository: keep the newest (max - 1) other sessions.
            var keepOthers = Math.Max(0, Math.Max(1, _jwtOptions.MaxActiveSessions) - 1);

            var activeTokens = (await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(token =>
                    token.UserId == userId &&
                    !token.IsRevoked &&
                    token.ExpiresAt > now)
                .ToListAsync(ct))
                .OrderByDescending(token => token.CreatedAt)
                .Skip(keepOthers);

            foreach (var token in activeTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = now;
                token.RevokedByIp = createdByIp;
                token.ReplacedByTokenHash = RefreshTokenMarkers.Superseded;
            }
        }

        var entity = new RefreshToken
        {
            TokenHash = newTokenHash,
            UserId = userId,
            ExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays),
            CreatedByIp = createdByIp
        };

        _db.RefreshTokens.Add(entity);

        await _db.SaveChangesAsync(ct);

        if (!endOtherSessions)
        {
            // Same rule as the real repository: a grace-window sibling must not push the live sessions past the limit.
            var keepOthers = Math.Max(0, Math.Max(1, _jwtOptions.MaxActiveSessions) - 1);

            var excess = (await _db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(token =>
                    token.UserId == userId &&
                    !token.IsRevoked &&
                    token.ExpiresAt > now &&
                    token.Id != entity.Id)
                .ToListAsync(ct))
                .OrderByDescending(token => token.CreatedAt)
                .Skip(keepOthers);

            var ended = false;
            foreach (var token in excess)
            {
                token.IsRevoked = true;
                token.RevokedAt = now;
                token.RevokedByIp = createdByIp;
                token.ReplacedByTokenHash = RefreshTokenMarkers.Superseded;
                ended = true;
            }

            if (ended)
                await _db.SaveChangesAsync(ct);
        }
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
