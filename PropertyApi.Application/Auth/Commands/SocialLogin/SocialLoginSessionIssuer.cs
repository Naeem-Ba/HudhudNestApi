using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Auth.Commands.SocialLogin;

public sealed class SocialLoginSessionIssuer
{
    private readonly ISocialLoginIdentityService _identity;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtTokenSettings _jwtSettings;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<SocialLoginSessionIssuer> _logger;

    public SocialLoginSessionIssuer(
        ISocialLoginIdentityService identity,
        ITokenService tokenService,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenSettings jwtSettings,
        IAuditLogService auditLogs,
        ILogger<SocialLoginSessionIssuer> logger)
    {
        _identity = identity;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _jwtSettings = jwtSettings;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<SocialLoginResult> SignInAsync(
        IdentityAccountSnapshot identity,
        string? ipAddress,
        CancellationToken ct)
    {
        if (identity.IsDeleted)
        {
            return SocialLoginResult.Failed(
                "The account is not available.");
        }

        var now =
            DateTime.UtcNow;

        var updateResult =
            await _identity.RecordSuccessfulLoginAsync(
                identity.IdentityId,
                now,
                ct);

        if (!updateResult.Succeeded)
        {
            _logger.LogWarning(
                "Failed updating social-login identity {IdentityId}. Errors: {Errors}",
                identity.IdentityId,
                string.Join(
                    ", ",
                    updateResult.Errors));

            return SocialLoginResult.Failed(
                "Could not update the account state.");
        }

        var roles =
            await _identity.GetRolesAsync(
                identity.IdentityId,
                ct);

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
            ipAddress,
            ct);

        await _auditLogs.LogAsync(
            userId:
                identity.IdentityId,

            action:
                AuditActions.Login,

            ipAddress:
                ipAddress,

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

                        provider =
                            "Social",

                        success =
                            true,

                        timestamp =
                            DateTime.UtcNow
                    }),

            ct:
                ct);

        return SocialLoginResult.Ok(
            accessToken,
            refreshToken,
            _jwtSettings.AccessTokenMinutes * 60);
    }
}
