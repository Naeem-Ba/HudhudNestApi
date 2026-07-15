using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Auth.Commands.RefreshToken;

public sealed class RefreshTokenReuseHandler
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IRefreshTokenIdentityService _identity;
    private readonly IUserSecurityStampCacheInvalidator
        _securityStampCacheInvalidator;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<RefreshTokenReuseHandler> _logger;

    public RefreshTokenReuseHandler(
        IRefreshTokenRepository refreshTokens,
        IRefreshTokenIdentityService identity,
        IUserSecurityStampCacheInvalidator securityStampCacheInvalidator,
        IAuditLogService auditLogs,
        ILogger<RefreshTokenReuseHandler> logger)
    {
        _refreshTokens = refreshTokens;
        _identity = identity;
        _securityStampCacheInvalidator =
            securityStampCacheInvalidator;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task HandleAsync(
        RefreshTokenRecord stored,
        string? ipAddress,
        CancellationToken ct)
    {
        var now =
            DateTime.UtcNow;

        await _refreshTokens.RevokeActiveTokensForUserAsync(
            stored.UserId,
            now,
            ipAddress,
            ct);

        var identity =
            await _identity.FindByIdAsync(
                stored.UserId,
                ct);

        if (identity is not null &&
            !identity.IsDeleted)
        {
            var stampResult =
                await _identity.UpdateSecurityStampAsync(
                    identity.IdentityId,
                    ct);

            if (!stampResult.Succeeded)
            {
                _logger.LogWarning(
                    "Security stamp update failed after refresh-token reuse detection for user {UserId}. Errors: {Errors}",
                    stored.UserId,
                    string.Join(
                        ", ",
                        stampResult.Errors));
            }
        }

        await _securityStampCacheInvalidator.InvalidateAsync(
            stored.UserId,
            ct);

        await _auditLogs.LogAsync(
            userId:
                stored.UserId,

            action:
                AuditActions.RefreshTokenReuseDetected,

            ipAddress:
                ipAddress,

            oldValue:
                JsonSerializer.Serialize(
                    new
                    {
                        refreshTokenId =
                            stored.Id,

                        wasRevoked =
                            stored.IsRevoked,

                        expiresAt =
                            stored.ExpiresAt
                    }),

            newValue:
                JsonSerializer.Serialize(
                    new
                    {
                        activeRefreshTokensRevoked =
                            true,

                        accessTokensInvalidatedBySecurityStamp =
                            true,

                        timestamp =
                            now
                    }),

            ct:
                ct);

        _logger.LogWarning(
            "Refresh token reuse detected for user {UserId}. Active refresh tokens revoked and security stamp invalidated.",
            stored.UserId);
    }
}
