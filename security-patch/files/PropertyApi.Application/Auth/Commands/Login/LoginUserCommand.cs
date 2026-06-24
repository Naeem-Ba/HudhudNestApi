using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Auth.Commands.Login;

public sealed record LoginCommand(
    string Email,
    string Password,
    string? IpAddress = null) : IRequest<LoginResult>;

public sealed record LoginResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }

    public static LoginResult Ok(string accessToken, string refreshToken, int expiresIn) => new()
    {
        Success = true,
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        ExpiresIn = expiresIn
    };

    public static LoginResult InvalidCredentials() => new()
    {
        Success = false,
        Message = "Invalid credentials."
    };
}

public sealed class LoginCommandHandler
    : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtTokenSettings _jwtSettings;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IIdentityUserService identityUsers,
        ITokenService tokenService,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenSettings jwtSettings,
        IAuditLogService auditLogs,
        ILogger<LoginCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _jwtSettings = jwtSettings;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<LoginResult> Handle(
        LoginCommand request,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _identityUsers.FindByEmailAsync(email, ct);

        if (user is null || user.IsDeleted)
        {
            _logger.LogWarning("Login failed for {Email}: user not found or deleted.", email);
            return LoginResult.InvalidCredentials();
        }

        var passwordValid = await _identityUsers.CheckPasswordAsync(user, request.Password, ct);
        if (!passwordValid)
        {
            _logger.LogWarning("Login failed for user {UserId}: invalid password.", user.Id);
            return LoginResult.InvalidCredentials();
        }

        var roles = await _identityUsers.GetRolesAsync(user, ct);
        var accessToken = _tokenService.GenerateAccessToken(user, roles);
        var refreshToken = _tokenService.GenerateRefreshToken();

        await _refreshTokens.AddAsync(user.Id, refreshToken, request.IpAddress, ct);

        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _identityUsers.UpdateAsync(user, ct);

        await _auditLogs.LogAsync(
            userId: user.Id,
            action: AuditActions.Login,
            ipAddress: request.IpAddress,
            oldValue: null,
            newValue: JsonSerializer.Serialize(new
            {
                userId = user.Id,
                email = user.Email,
                success = true,
                timestamp = DateTime.UtcNow
            }),
            ct: ct);

        _logger.LogInformation("User {UserId} logged in successfully.", user.Id);

        return LoginResult.Ok(
            accessToken,
            refreshToken,
            _jwtSettings.AccessTokenMinutes * 60);
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
