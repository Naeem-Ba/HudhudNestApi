using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Users.Entities;

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
    private readonly UserManager<User> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtTokenSettings _jwtSettings;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        UserManager<User> userManager,
        ITokenService tokenService,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenSettings jwtSettings,
        ILogger<LoginCommandHandler> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    public async Task<LoginResult> Handle(
        LoginCommand request,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null || user.IsDeleted)
        {
            _logger.LogWarning("Login failed for {Email}: user not found or deleted.", email);
            return LoginResult.InvalidCredentials();
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            _logger.LogWarning("Login failed for user {UserId}: invalid password.", user.Id);
            return LoginResult.InvalidCredentials();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.GenerateAccessToken(user, roles.ToArray());
        var refreshToken = _tokenService.GenerateRefreshToken();

        await _refreshTokens.AddAsync(user.Id, refreshToken, request.IpAddress, ct);

        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

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
