using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Application.Auth.Commands.RefreshToken;

public sealed record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress = null) : IRequest<RefreshTokenResult>;

public sealed record RefreshTokenResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }

    public static RefreshTokenResult Ok(string accessToken, string refreshToken, int expiresIn) => new()
    {
        Success = true,
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        ExpiresIn = expiresIn
    };

    public static RefreshTokenResult Unauthorized(string message) => new()
    {
        Success = false,
        Message = message
    };
}

public sealed class RefreshTokenCommandHandler
    : IRequestHandler<RefreshTokenCommand, RefreshTokenResult>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIdentityUserService _identityUsers;
    private readonly ITokenService _tokenService;
    private readonly IJwtTokenSettings _jwtSettings;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IIdentityUserService identityUsers,
        ITokenService tokenService,
        IJwtTokenSettings jwtSettings,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _refreshTokens = refreshTokens;
        _identityUsers = identityUsers;
        _tokenService = tokenService;
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    public async Task<RefreshTokenResult> Handle(
        RefreshTokenCommand request,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        return await _refreshTokens.ExecuteInTransactionAsync(async tokenCt =>
        {
            var stored = await _refreshTokens.GetByRefreshTokenAsync(
                request.RefreshToken,
                tokenCt);

            if (stored is null ||
                stored.IsRevoked ||
                stored.ExpiresAt <= now ||
                stored.User is null ||
                stored.User.IsDeleted)
            {
                _logger.LogWarning("Refresh token rejected: invalid or expired.");
                return RefreshTokenResult.Unauthorized("Invalid or expired refresh token.");
            }

            var newRefreshToken = _tokenService.GenerateRefreshToken();

            var revoked = await _refreshTokens.RevokeIfActiveAsync(
                stored.Id,
                now,
                request.IpAddress,
                replacedByRefreshToken: newRefreshToken,
                tokenCt);

            if (!revoked)
            {
                _logger.LogWarning("Refresh token reuse detected for token {TokenId}.", stored.Id);
                return RefreshTokenResult.Unauthorized("Refresh token was already used.");
            }

            await _refreshTokens.AddAsync(
                stored.UserId,
                newRefreshToken,
                request.IpAddress,
                tokenCt);

            var roles = await _identityUsers.GetRolesAsync(stored.User, tokenCt);
            var accessToken = _tokenService.GenerateAccessToken(stored.User, roles);

            return RefreshTokenResult.Ok(
                accessToken,
                newRefreshToken,
                _jwtSettings.AccessTokenMinutes * 60);
        }, ct);
    }
}

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}
