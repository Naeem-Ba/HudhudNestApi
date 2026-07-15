using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Identity.Services;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.TestInfrastructure;

internal sealed class EfInMemoryRefreshTokenRepository
    : IRefreshTokenRepository, IRefreshTokenStore
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

    public async Task StoreAsync(
        Guid userId,
        string refreshToken,
        string? createdByIp,
        CancellationToken ct = default)
    {
        await AddCoreAsync(
            userId,
            refreshToken,
            createdByIp,
            setReplacementHash: false,
            ct);
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
            setReplacementHash: true,
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
            token.IsRevoked);
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
        bool setReplacementHash,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token is required.", nameof(refreshToken));

        var now = DateTime.UtcNow;
        var newTokenHash = HashToken(refreshToken);

        var activeTokens = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token =>
                token.UserId == userId &&
                !token.IsRevoked &&
                token.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
            token.RevokedByIp = createdByIp;

            if (setReplacementHash)
            {
                token.ReplacedByTokenHash = newTokenHash;
            }
        }

        _db.RefreshTokens.Add(
            new RefreshToken
            {
                TokenHash = newTokenHash,
                UserId = userId,
                ExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays),
                CreatedByIp = createdByIp
            });

        await _db.SaveChangesAsync(ct);
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
