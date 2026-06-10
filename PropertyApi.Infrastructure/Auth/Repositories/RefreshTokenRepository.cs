using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Identity.Services;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Auth.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwtOptions;

    public RefreshTokenRepository(
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
        _db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = HashToken(refreshToken),
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            CreatedByIp = createdByIp
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<RefreshTokenRecord?> GetByRefreshTokenAsync(
        string refreshToken,
        CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);

        var token = await _db.RefreshTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);

        return token is null
            ? null
            : new RefreshTokenRecord(
                token.Id,
                token.UserId,
                token.ExpiresAt,
                token.IsRevoked,
                token.User);
    }

    public async Task<bool> RevokeIfActiveAsync(
        Guid tokenId,
        DateTime now,
        string? revokedByIp,
        string? replacedByRefreshToken,
        CancellationToken ct = default)
    {
        var replacementHash = string.IsNullOrWhiteSpace(replacedByRefreshToken)
            ? null
            : HashToken(replacedByRefreshToken);

        var affectedRows = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token =>
                token.Id == tokenId &&
                !token.IsRevoked &&
                token.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.IsRevoked, true)
                .SetProperty(token => token.RevokedAt, now)
                .SetProperty(token => token.RevokedByIp, revokedByIp)
                .SetProperty(token => token.ReplacedByTokenHash, replacementHash), ct);

        return affectedRows == 1;
    }

    public async Task RevokeActiveTokensForUserAsync(
        Guid userId,
        DateTime now,
        string? revokedByIp,
        CancellationToken ct = default)
    {
        await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token =>
                token.UserId == userId &&
                !token.IsRevoked &&
                token.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.IsRevoked, true)
                .SetProperty(token => token.RevokedAt, now)
                .SetProperty(token => token.RevokedByIp, revokedByIp), ct);
    }

    public async Task<bool> RevokeUserTokenAsync(
        Guid userId,
        string refreshToken,
        DateTime now,
        string? revokedByIp,
        CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);

        var affectedRows = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token =>
                token.TokenHash == hash &&
                token.UserId == userId &&
                !token.IsRevoked &&
                token.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.IsRevoked, true)
                .SetProperty(token => token.RevokedAt, now)
                .SetProperty(token => token.RevokedByIp, revokedByIp), ct);

        return affectedRows == 1;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
