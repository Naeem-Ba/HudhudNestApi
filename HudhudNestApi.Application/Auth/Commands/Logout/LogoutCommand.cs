using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Auth.Commands.Logout;

// UserId is null when the caller's access token was missing or expired and only the refresh
// token (HttpOnly cookie) identifies the session; the handler then resolves the owner from it.
public sealed record LogoutCommand(
    Guid? UserId,
    string RefreshToken,
    string? IpAddress = null)
    : IRequest;

public sealed class LogoutCommandHandler
    : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ILogoutIdentityService _identity;
    private readonly IUserSecurityStampCacheInvalidator
        _securityStampCacheInvalidator;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokens,
        ILogoutIdentityService identity,
        IUserSecurityStampCacheInvalidator securityStampCacheInvalidator,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokens = refreshTokens;
        _identity = identity;
        _securityStampCacheInvalidator =
            securityStampCacheInvalidator;
        _logger = logger;
    }

    public async Task Handle(
        LogoutCommand request,
        CancellationToken ct)
    {
        var userId = request.UserId;

        if (userId is null)
        {
            // No usable access token. Only a live refresh token proves who is logging out; an
            // unknown, revoked or expired one changes nothing (the controller still clears the
            // cookie).
            var stored =
                await _refreshTokens.GetByRefreshTokenAsync(
                    request.RefreshToken,
                    ct);

            if (stored is null ||
                stored.IsRevoked ||
                stored.ExpiresAt <= DateTime.UtcNow)
            {
                return;
            }

            userId = stored.UserId;
        }

        var revoked =
            await _refreshTokens.RevokeUserTokenAsync(
                userId.Value,
                request.RefreshToken,
                DateTime.UtcNow,
                request.IpAddress,
                ct);

        var identity =
            await _identity.FindByIdAsync(
                userId.Value,
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
                    "Security stamp update failed during logout for identity {IdentityId}. Errors: {Errors}",
                    identity.IdentityId,
                    string.Join(
                        ", ",
                        stampResult.Errors));
            }
        }

        /*
         * Always invalidate the cached security stamp.
         *
         * This is required both when:
         * - the Identity security stamp was successfully changed;
         * - the Identity update failed;
         * - the Identity account no longer exists;
         * - the Identity account is soft-deleted.
         *
         * The cache must never retain a stale authentication stamp
         * after an explicit logout operation.
         */
        await _securityStampCacheInvalidator.InvalidateAsync(
            userId.Value,
            ct);

        if (revoked)
        {
            _logger.LogInformation(
                "Identity {IdentityId} logged out, refresh token was revoked, and security-stamp cache was invalidated.",
                userId.Value);
        }
    }
}

public sealed class LogoutCommandValidator
    : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEqual(Guid.Empty);

        RuleFor(x => x.RefreshToken)
            .NotEmpty();
    }
}
