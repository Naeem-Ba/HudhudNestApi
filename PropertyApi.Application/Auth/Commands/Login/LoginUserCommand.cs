using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Auth.Commands.Login;

public sealed record LoginCommand(
    string Email,
    string Password,
    string? IpAddress = null)
    : IRequest<LoginResult>;

public sealed record LoginResult
{
    public bool Success { get; init; }

    public string Message { get; init; } =
        string.Empty;

    public string AccessToken { get; init; } =
        string.Empty;

    public string RefreshToken { get; init; } =
        string.Empty;

    public int ExpiresIn { get; init; }

    public static LoginResult Ok(
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

    public static LoginResult InvalidCredentials()
        => new()
        {
            Success = false,
            Message = "Invalid credentials."
        };
}

public sealed class LoginCommandHandler
    : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IPureIdentityService _identity;

    private readonly ITokenService _tokenService;

    private readonly IRefreshTokenRepository _refreshTokens;

    private readonly IJwtTokenSettings _jwtSettings;

    private readonly IAuditLogService _auditLogs;

    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IPureIdentityService identity,
        ITokenService tokenService,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenSettings jwtSettings,
        IAuditLogService auditLogs,
        ILogger<LoginCommandHandler> logger)
    {
        _identity = identity;
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
        var email =
            NormalizeEmail(request.Email);

        /*
         * Resolve the authentication account through the
         * framework-neutral Identity boundary.
         */
        var identity =
            await _identity.FindByEmailAsync(
                email,
                ct);

        if (identity is null ||
            identity.IsDeleted)
        {
            _logger.LogWarning(
                "Login failed for {Email}: identity not found or deleted.",
                email);

            return LoginResult.InvalidCredentials();
        }

        var passwordValid =
            await _identity.CheckPasswordAsync(
                identity.IdentityId,
                request.Password,
                ct);

        if (!passwordValid)
        {
            _logger.LogWarning(
                "Login failed for identity {IdentityId}: invalid password.",
                identity.IdentityId);

            return LoginResult.InvalidCredentials();
        }

        var roles =
            await _identity.GetRolesAsync(
                identity.IdentityId,
                ct);

        /*
         * JWT generation depends only on a neutral token subject.
         *
         * The handler does not know whether the underlying implementation
         * currently uses the legacy User entity or the future
         * ApplicationUser entity.
         */
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
            _tokenService.GenerateAccessToken(
                tokenSubject,
                roles);

        var refreshToken =
            _tokenService.GenerateRefreshToken();

        await _refreshTokens.AddAsync(
            identity.IdentityId,
            refreshToken,
            request.IpAddress,
            ct);

        var loginAtUtc =
            DateTime.UtcNow;

        /*
         * Persist authentication activity through the neutral
         * Identity boundary.
         *
         * Preserve the previous behavior:
         * a persistence validation failure is logged but does not
         * invalidate credentials that were already successfully verified.
         */
        var recordLoginResult =
            await _identity.RecordSuccessfulLoginAsync(
                identity.IdentityId,
                loginAtUtc,
                ct);

        if (!recordLoginResult.Succeeded)
        {
            _logger.LogWarning(
                "Failed to record successful login timestamp for identity {IdentityId}. Errors: {Errors}",
                identity.IdentityId,
                string.Join(
                    ", ",
                    recordLoginResult.Errors));
        }

        await _auditLogs.LogAsync(
            userId:
                identity.IdentityId,

            action:
                AuditActions.Login,

            ipAddress:
                request.IpAddress,

            oldValue:
                null,

            newValue:
                JsonSerializer.Serialize(
                    new
                    {
                        userId =
                            identity.IdentityId,

                        email =
                            identity.Email,

                        success =
                            true,

                        timestamp =
                            loginAtUtc
                    }),

            ct:
                ct);

        _logger.LogInformation(
            "Identity {IdentityId} logged in successfully.",
            identity.IdentityId);

        return LoginResult.Ok(
            accessToken,
            refreshToken,
            _jwtSettings.AccessTokenMinutes *
            60);
    }

    private static string NormalizeEmail(
        string email)
        => email
            .Trim()
            .ToLowerInvariant();
}

public sealed class LoginCommandValidator
    : AbstractValidator<LoginCommand>
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