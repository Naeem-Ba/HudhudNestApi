using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Auth.Commands.RefreshToken;

public sealed record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress = null)
    : IRequest<RefreshTokenResult>;

public sealed record RefreshTokenResult
{
    public bool Success { get; init; }

    public string Message { get; init; } =
        string.Empty;

    public string AccessToken { get; init; } =
        string.Empty;

    public string RefreshToken { get; init; } =
        string.Empty;

    public int ExpiresIn { get; init; }

    public static RefreshTokenResult Ok(
        string accessToken,
        string refreshToken,
        int expiresIn)
        => new()
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = expiresIn
        };

    public static RefreshTokenResult Unauthorized(
        string message)
        => new()
        {
            Success = false,
            Message = message
        };
}

public sealed class RefreshTokenCommandHandler
    : IRequestHandler<
        RefreshTokenCommand,
        RefreshTokenResult>
{
    private readonly IRefreshTokenRepository
        _refreshTokens;

    private readonly IPureIdentityService
        _identity;

    private readonly ITokenService
        _tokenService;

    private readonly IJwtTokenSettings
        _jwtSettings;

    private readonly IUserSecurityStampCacheInvalidator
        _securityStampCacheInvalidator;

    private readonly IAuditLogService
        _auditLogs;

    private readonly ILogger<RefreshTokenCommandHandler>
        _logger;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IPureIdentityService identity,
        ITokenService tokenService,
        IJwtTokenSettings jwtSettings,
        IUserSecurityStampCacheInvalidator
            securityStampCacheInvalidator,
        IAuditLogService auditLogs,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _refreshTokens =
            refreshTokens;

        _identity =
            identity;

        _tokenService =
            tokenService;

        _jwtSettings =
            jwtSettings;

        _securityStampCacheInvalidator =
            securityStampCacheInvalidator;

        _auditLogs =
            auditLogs;

        _logger =
            logger;
    }

    public async Task<RefreshTokenResult> Handle(
        RefreshTokenCommand request,
        CancellationToken ct)
    {
        var now =
            DateTime.UtcNow;

        return await _refreshTokens
            .ExecuteInTransactionAsync(
                async tokenCt =>
                {
                    var stored =
                        await _refreshTokens
                            .GetByRefreshTokenAsync(
                                request.RefreshToken,
                                tokenCt);

                    if (stored is null)
                    {
                        _logger.LogWarning(
                            "Refresh token rejected: token hash was not found.");

                        return RefreshTokenResult
                            .Unauthorized(
                                "Invalid or expired refresh token.");
                    }

                    if (stored.IsRevoked)
                    {
                        await HandleRefreshTokenReuseAsync(
                            stored,
                            request.IpAddress,
                            tokenCt);

                        return RefreshTokenResult
                            .Unauthorized(
                                "Refresh token reuse detected. All active sessions were revoked.");
                    }

                    /*
                     * Resolve Identity state through the neutral boundary.
                     *
                     * RefreshTokenRecord no longer exposes an Identity entity.
                     */
                    var identity =
                        await _identity.FindByIdAsync(
                            stored.UserId,
                            tokenCt);

                    if (stored.ExpiresAt <= now ||
                        identity is null ||
                        identity.IsDeleted)
                    {
                        _logger.LogWarning(
                            "Refresh token rejected for user {UserId}: expired, missing identity, or deleted identity.",
                            stored.UserId);

                        return RefreshTokenResult
                            .Unauthorized(
                                "Invalid or expired refresh token.");
                    }

                    var newRefreshToken =
                        _tokenService
                            .GenerateRefreshToken();

                    var revoked =
                        await _refreshTokens
                            .RevokeIfActiveAsync(
                                stored.Id,
                                now,
                                request.IpAddress,
                                replacedByRefreshToken:
                                    newRefreshToken,
                                tokenCt);

                    if (!revoked)
                    {
                        await HandleRefreshTokenReuseAsync(
                            stored,
                            request.IpAddress,
                            tokenCt);

                        return RefreshTokenResult
                            .Unauthorized(
                                "Refresh token reuse detected. All active sessions were revoked.");
                    }

                    await _refreshTokens.AddAsync(
                        stored.UserId,
                        newRefreshToken,
                        request.IpAddress,
                        tokenCt);

                    var roles =
                        await _identity.GetRolesAsync(
                            identity.IdentityId,
                            tokenCt);

                    var tokenSubject =
                        new AccessTokenSubject(
                            IdentityId:
                                identity.IdentityId,

                            Email:
                                identity.Email,

                            UserName:
                                identity.UserName,

                            SecurityStamp:
                                identity.SecurityStamp);

                    var accessToken =
                        _tokenService
                            .GenerateAccessToken(
                                tokenSubject,
                                roles);

                    return RefreshTokenResult.Ok(
                        accessToken,
                        newRefreshToken,
                        _jwtSettings
                            .AccessTokenMinutes *
                        60);
                },
                ct);
    }

    private async Task HandleRefreshTokenReuseAsync(
        RefreshTokenRecord stored,
        string? ipAddress,
        CancellationToken ct)
    {
        var now =
            DateTime.UtcNow;

        /*
         * Revoke all currently active refresh tokens first.
         */
        await _refreshTokens
            .RevokeActiveTokensForUserAsync(
                stored.UserId,
                now,
                ipAddress,
                ct);

        /*
         * Resolve the Identity account separately.
         *
         * Missing or deleted identities do not block refresh-token
         * revocation or audit logging.
         */
        var identity =
            await _identity.FindByIdAsync(
                stored.UserId,
                ct);

        if (identity is not null &&
            !identity.IsDeleted)
        {
            var stampResult =
                await _identity
                    .UpdateSecurityStampAsync(
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

        /*
         * Invalidate the distributed/in-memory stamp cache even when
         * the Identity account cannot be resolved.
         */
        await _securityStampCacheInvalidator
            .InvalidateAsync(
                stored.UserId,
                ct);

        await _auditLogs.LogAsync(
            userId:
                stored.UserId,

            action:
                AuditActions
                    .RefreshTokenReuseDetected,

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

public sealed class RefreshTokenCommandValidator
    : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty();
    }
}