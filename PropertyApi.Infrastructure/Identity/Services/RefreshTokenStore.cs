using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class RefreshTokenStore : IRefreshTokenStore
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwtOptions;

    public RefreshTokenStore(
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
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token is required.", nameof(refreshToken));

        var now = DateTime.UtcNow;
        await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.UserId == userId && !token.IsRevoked && token.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.IsRevoked, true)
                .SetProperty(token => token.RevokedAt, now)
                .SetProperty(token => token.RevokedByIp, createdByIp), ct);

        _db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = HashToken(refreshToken),
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