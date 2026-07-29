using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;

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
                        await _reuseHandler.HandleAsync(
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
                        return RefreshTokenResult
                            .Unauthorized(
                                "Refresh token was already rotated by another request.");
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
