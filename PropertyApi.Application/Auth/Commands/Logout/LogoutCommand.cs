using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Auth.Commands.Logout;

public sealed record LogoutCommand(
    Guid UserId,
    string RefreshToken,
    string? IpAddress = null) : IRequest;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokens,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokens = refreshTokens;
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

        if (revoked)
            _logger.LogInformation("User {UserId} logged out and refresh token was revoked.", request.UserId);
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

