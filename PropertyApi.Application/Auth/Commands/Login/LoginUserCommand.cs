using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Abstractions;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;

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

    public static LoginResult Ok(
        string accessToken,
        string refreshToken,
        int expiresIn) =>
        new()
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = expiresIn
        };

    public static LoginResult InvalidCredentials() =>
        new() { Message = "Invalid credentials." };
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly ILoginIdentityService _identity;
    private readonly IAuthenticationSessionIssuer _sessions;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        ILoginIdentityService identity,
        IAuthenticationSessionIssuer sessions,
        ILogger<LoginCommandHandler> logger)
    {
        _identity = identity;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task<LoginResult> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var identity = await _identity.FindByEmailAsync(email, cancellationToken);
        if (identity is null || identity.IsDeleted ||
            !await _identity.CheckPasswordAsync(
                identity.IdentityId,
                request.Password,
                cancellationToken))
        {
            _logger.LogWarning("Password authentication rejected.");
            return LoginResult.InvalidCredentials();
        }

        var session = await _sessions.IssueAsync(
            new AuthenticationSessionRequest(
                identity,
                "password",
                request.IpAddress,
                SuccessfulLoginRecordingMode.BestEffort),
            cancellationToken);
        return session.Succeeded
            ? LoginResult.Ok(
                session.AccessToken!,
                session.RefreshToken!,
                session.ExpiresInSeconds)
            : LoginResult.InvalidCredentials();
    }
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
