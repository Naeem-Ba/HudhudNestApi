using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;

public sealed class PhoneOtpSessionIssuer
{
    private readonly IPhoneOtpIdentityService _identity;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly ILogger<PhoneOtpSessionIssuer> _logger;

    public PhoneOtpSessionIssuer(
        IPhoneOtpIdentityService identity,
        ITokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        ILogger<PhoneOtpSessionIssuer> logger)
    {
        _identity = identity;
        _tokenService = tokenService;
        _refreshTokenStore = refreshTokenStore;
        _logger = logger;
    }

    public async Task<VerifyOtpResult> IssueAsync(
        IdentityAccountSnapshot identity,
        UserAccount? account,
        bool isNewUser,
        string phone,
        string? ipAddress,
        CancellationToken ct)
    {
        var roles =
            await _identity.GetRolesAsync(
                identity.IdentityId,
                ct);

        if (isNewUser &&
            !roles.Contains(
                RoleNames.User,
                StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "New phone identity {IdentityId} does not have the required default role.",
                identity.IdentityId);

            return VerifyOtpResult.Fail(
                "ROLE_ASSIGNMENT_FAILED",
                "Could not initialize the account.");
        }

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
                roles.ToArray());

        var refreshToken =
            _tokenService.GenerateRefreshToken();

        await _refreshTokenStore.StoreAsync(
            identity.IdentityId,
            refreshToken,
            ipAddress,
            ct);

        return VerifyOtpResult.Ok(
            isNewUser:
                isNewUser,

            accessToken:
                accessToken,

            refreshToken:
                refreshToken,

            expiresAt:
                _tokenService.GetAccessTokenExpiresAtUtc(),

            user:
                new UserProfileDto
                {
                    Id =
                        identity.UserAccountId,

                    PhoneNumber =
                        identity.PhoneNumber
                        ?? phone,

                    Email =
                        identity.Email,

                    DisplayName =
                        account?.DisplayName,

                    FirstName =
                        account?.FirstName
                        ?? string.Empty,

                    LastName =
                        account?.LastName
                        ?? string.Empty,

                    HasEmail =
                        !string.IsNullOrWhiteSpace(
                            identity.Email),

                    HasPassword =
                        identity.HasPassword,

                    EmailVerified =
                        identity.EmailConfirmed
                });
    }
}
