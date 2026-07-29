using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Abstractions;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Observability;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Auth.Services;

public sealed class AuthenticationSessionIssuer : IAuthenticationSessionIssuer
{
    private readonly ILoginIdentityService _identity;
    private readonly ITokenService _tokens;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtTokenSettings _settings;
    private readonly IAuditLogService _audit;
    private readonly ILogger<AuthenticationSessionIssuer> _logger;

    public AuthenticationSessionIssuer(
        ILoginIdentityService identity,
        ITokenService tokens,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenSettings settings,
        IAuditLogService audit,
        ILogger<AuthenticationSessionIssuer> logger)
    {
        _identity = identity;
        _tokens = tokens;
        _refreshTokens = refreshTokens;
        _settings = settings;
        _audit = audit;
        _logger = logger;
    }

    public async Task<AuthenticationSessionResult> IssueAsync(
        AuthenticationSessionRequest request,
        CancellationToken cancellationToken)
    {
        var identity = request.Identity;
        if (identity.IsDeleted ||
            identity.IsBanned ||
            identity.LockoutEndUtc > DateTimeOffset.UtcNow)
        {
            ApplicationTelemetry.RecordAuthenticationStage("session_issuance", "rejected", request.AuthenticationMethod);
            return AuthenticationSessionResult.Failed("ACCOUNT_UNAVAILABLE");
        }

        var now = DateTime.UtcNow;
        if (request.LoginRecordingMode != SuccessfulLoginRecordingMode.None)
        {
            var recorded = await _identity.RecordSuccessfulLoginAsync(
                identity.IdentityId,
                now,
                cancellationToken);

            if (!recorded.Succeeded)
            {
                _logger.LogWarning(
                    "Authentication session state update failed. Method={AuthenticationMethod}, IdentityId={IdentityId}, ErrorCount={ErrorCount}",
                    request.AuthenticationMethod,
                    identity.IdentityId,
                    recorded.Errors.Count);

                if (request.LoginRecordingMode == SuccessfulLoginRecordingMode.Required)
                {
                    ApplicationTelemetry.RecordAuthenticationStage(
                        "session_issuance", "state_update_failed", request.AuthenticationMethod);
                    return AuthenticationSessionResult.Failed("SESSION_STATE_UPDATE_FAILED");
                }
            }
        }

        var roles = await _identity.GetRolesAsync(identity.IdentityId, cancellationToken);
        var subject = new AccessTokenSubject(
            identity.IdentityId,
            identity.Email,
            identity.UserName,
            identity.SecurityStamp);

        var accessToken = _tokens.GenerateAccessToken(subject, roles);
        var refreshToken = _tokens.GenerateRefreshToken();
        await _refreshTokens.AddAsync(
            identity.IdentityId,
            refreshToken,
            request.IpAddress,
            cancellationToken);

        await _audit.LogAsync(
            identity.IdentityId,
            AuditActions.Login,
            request.IpAddress,
            null,
            JsonSerializer.Serialize(new
            {
                authenticationMethod = request.AuthenticationMethod,
                success = true,
                timestamp = now
            }),
            cancellationToken);

        _logger.LogInformation(
            "Authentication session issued. Method={AuthenticationMethod}, IdentityId={IdentityId}",
            request.AuthenticationMethod,
            identity.IdentityId);

        ApplicationTelemetry.RecordAuthenticationStage("session_issuance", "success", request.AuthenticationMethod);

        return new AuthenticationSessionResult(
            true,
            accessToken,
            refreshToken,
            _tokens.GetAccessTokenExpiresAtUtc(),
            _settings.AccessTokenMinutes * 60);
    }
}
