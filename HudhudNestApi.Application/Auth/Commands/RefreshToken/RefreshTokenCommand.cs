using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Auth.Commands.RefreshToken;

public sealed record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress = null)
    : IRequest<RefreshTokenResult>;

public sealed record RefreshTokenResult
{
    public bool Success { get; init; }

    public string Message { get; init; } =
        string.Empty;

    public string? ErrorCode { get; init; }

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
        string message,
        string errorCode)
        => new()
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode
        };
}

/// <summary>
/// Stable codes for <see cref="RefreshTokenResult.ErrorCode"/> — lets the client distinguish a
/// silently-recoverable-elsewhere case from one that genuinely means "log in again", instead of
/// parsing the free-text <see cref="RefreshTokenResult.Message"/>.
/// </summary>
public static class RefreshTokenErrorCodes
{
    public const string Expired = "REFRESH_TOKEN_EXPIRED";
    public const string ReuseDetected = "REFRESH_TOKEN_REUSE_DETECTED";
    public const string RotationConflict = "REFRESH_TOKEN_ROTATION_CONFLICT";

    /// <summary>
    /// The session was ended by design, not by theft: the user signed in on more devices than allowed, so the
    /// oldest one was dropped. The client shows "signed in on another device" and nothing else is revoked.
    /// </summary>
    public const string Superseded = "REFRESH_TOKEN_SUPERSEDED";

    /// <summary>
    /// The token was ended by a logout, a password change/reset or an account action. It is dead, but replaying it
    /// proves nothing about theft, so it neither revokes other sessions nor rotates the security stamp.
    /// </summary>
    public const string Revoked = "REFRESH_TOKEN_REVOKED";
}

public sealed class RefreshTokenCommandHandler
    : IRequestHandler<
        RefreshTokenCommand,
        RefreshTokenResult>
{
    private readonly IRefreshTokenRepository
        _refreshTokens;

    private readonly IRefreshTokenIdentityService
        _identity;

    private readonly ITokenService
        _tokenService;

    private readonly IJwtTokenSettings
        _jwtSettings;

    private readonly RefreshTokenReuseHandler
        _reuseHandler;

    private readonly ILogger<RefreshTokenCommandHandler>
        _logger;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IRefreshTokenIdentityService identity,
        ITokenService tokenService,
        IJwtTokenSettings jwtSettings,
        RefreshTokenReuseHandler reuseHandler,
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

        _reuseHandler =
            reuseHandler;

        _logger =
            logger;
    }

    private bool IsWithinReuseGrace(
        RefreshTokenRecord stored,
        DateTime now)
    {
        var grace = _jwtSettings.RefreshReuseGraceSeconds;

        return grace > 0 &&
               stored.RevokedAt is { } revokedAt &&
               now - revokedAt <= TimeSpan.FromSeconds(grace);
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
                                "Invalid or expired refresh token.",
                                RefreshTokenErrorCodes.Expired);
                    }

                    // A token that was ended on purpose is simply dead; only a token that was ROTATED away
                    // and is presented again looks like theft. Treating every revoked token as theft signed the
                    // user out of all devices after a password change, a logout elsewhere, or a sign-in on a
                    // further device (the old device refreshing with its ended token revoked the new one too).
                    var replayOfRotatedToken = false;

                    if (stored.IsRevoked)
                    {
                        if (stored.ReplacedByTokenHash == RefreshTokenMarkers.Superseded)
                        {
                            return RefreshTokenResult
                                .Unauthorized(
                                    "This session ended because you signed in on another device.",
                                    RefreshTokenErrorCodes.Superseded);
                        }

                        if (string.IsNullOrEmpty(stored.ReplacedByTokenHash))
                        {
                            return RefreshTokenResult
                                .Unauthorized(
                                    "This session has ended.",
                                    RefreshTokenErrorCodes.Revoked);
                        }

                        if (!IsWithinReuseGrace(stored, now))
                        {
                            await _reuseHandler.HandleAsync(
                                stored,
                                request.IpAddress,
                                tokenCt);

                            return RefreshTokenResult
                                .Unauthorized(
                                    "Refresh token reuse detected. All active sessions were revoked.",
                                    RefreshTokenErrorCodes.ReuseDetected);
                        }

                        // Rotated moments ago: the client most likely never received the answer (dropped
                        // connection, app suspended mid-request) or two requests raced. Answer it again.
                        replayOfRotatedToken = true;
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
                        identity.IsDeleted ||
                        identity.IsBanned)
                    {
                        _logger.LogWarning(
                            "Refresh token rejected for user {UserId}: expired, missing identity, or deleted/banned identity.",
                            stored.UserId);

                        return RefreshTokenResult
                            .Unauthorized(
                                "Invalid or expired refresh token.",
                                RefreshTokenErrorCodes.Expired);
                    }

                    var newRefreshToken =
                        _tokenService
                            .GenerateRefreshToken();

                    if (!replayOfRotatedToken)
                    {
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
                            // Lost a race against another refresh of the very same token (two tabs, or a
                            // retry overlapping the original). The winner already rotated it; with the grace
                            // window on, this caller gets its own fresh token instead of an error that would
                            // sign it out. With the window off (0) the former conflict answer is kept.
                            if (_jwtSettings.RefreshReuseGraceSeconds <= 0)
                            {
                                return RefreshTokenResult
                                    .Unauthorized(
                                        "Refresh token was already rotated by another request.",
                                        RefreshTokenErrorCodes.RotationConflict);
                            }

                            replayOfRotatedToken = true;
                        }
                    }

                    if (replayOfRotatedToken)
                    {
                        _logger.LogInformation(
                            "Refresh token for user {UserId} was presented again within the {Grace}s grace window; issuing a new token.",
                            stored.UserId,
                            _jwtSettings.RefreshReuseGraceSeconds);
                    }

                    // A refresh ends no other session: the phone refreshing must not sign the website out.
                    await _refreshTokens.AddRotatedAsync(
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
