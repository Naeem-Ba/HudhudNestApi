using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Application.Auth.Commands.Logout;

public sealed record LogoutCommand(
    Guid UserId,
    string RefreshToken,
    string? IpAddress = null) : IRequest;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIdentityUserService _identityUsers;
    private readonly IUserSecurityStampCacheInvalidator _securityStampCacheInvalidator;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IIdentityUserService identityUsers,
        IUserSecurityStampCacheInvalidator securityStampCacheInvalidator,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokens = refreshTokens;
        _identityUsers = identityUsers;
        _securityStampCacheInvalidator = securityStampCacheInvalidator;
        _logger = logger;
    }

    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        var revoked = await _refreshTokens.RevokeUserTokenAsync(
            request.UserId,
            request.RefreshToken,
            DateTime.UtcNow,
            request.IpAddress,
            ct);

        var user = await _identityUsers.FindByIdAsync(request.UserId, ct);
        if (user is not null && !user.IsDeleted)
        {
            var stampResult = await _identityUsers.UpdateSecurityStampAsync(user, ct);

            if (!stampResult.Succeeded)
            {
                _logger.LogWarning(
                    "Security stamp update failed during logout for user {UserId}. Errors: {Errors}",
                    request.UserId,
                    string.Join(", ", stampResult.Errors));

                // Fail closed for the cache even if Identity failed to persist a new stamp.
                await _securityStampCacheInvalidator.InvalidateAsync(request.UserId, ct);
            }
        }
        else
        {
            await _securityStampCacheInvalidator.InvalidateAsync(request.UserId, ct);
        }

        if (revoked)
            _logger.LogInformation("User {UserId} logged out, refresh token was revoked, and security-stamp cache was invalidated.", request.UserId);
    }
}

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

