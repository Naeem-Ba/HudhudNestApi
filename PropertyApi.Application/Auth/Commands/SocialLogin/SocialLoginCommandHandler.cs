using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.SocialLogin;

public sealed class SocialLoginCommandHandler
    : IRequestHandler<SocialLoginCommand, SocialLoginResult>
{
    private readonly IEnumerable<ISocialTokenVerifier> _verifiers;
    private readonly IIdentityUserService _identityUsers;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtTokenSettings _jwtSettings;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<SocialLoginCommandHandler> _logger;

    public SocialLoginCommandHandler(
        IEnumerable<ISocialTokenVerifier> verifiers,
        IIdentityUserService identityUsers,
        ITokenService tokenService,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenSettings jwtSettings,
        IAuditLogService auditLogs,
        ILogger<SocialLoginCommandHandler> logger)
    {
        _verifiers = verifiers;
        _identityUsers = identityUsers;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _jwtSettings = jwtSettings;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<SocialLoginResult> Handle(
        SocialLoginCommand request,
        CancellationToken ct)
    {
        var providerName = !string.IsNullOrWhiteSpace(request.GoogleIdToken)
            ? "Google"
            : !string.IsNullOrWhiteSpace(request.AppleIdentityToken)
                ? "Apple"
                : null;

        var rawToken = providerName switch
        {
            "Google" => request.GoogleIdToken,
            "Apple" => request.AppleIdentityToken,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(rawToken))
        {
            return SocialLoginResult.Failed("A Google or Apple identity token is required.");
        }

        var verifier = _verifiers.FirstOrDefault(v =>
            v.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));

        if (verifier is null)
        {
            _logger.LogError("No social token verifier is registered for provider {Provider}.", providerName);
            return SocialLoginResult.Failed("Social login is not configured for this provider.");
        }

        var socialUser = await verifier.VerifyAsync(rawToken, ct);
        if (socialUser is null)
        {
            _logger.LogWarning("Social token verification failed for provider {Provider}.", providerName);
            return SocialLoginResult.Failed("The social identity token is invalid or expired.");
        }

        var user = await _identityUsers.FindByLoginAsync(
            providerName,
            socialUser.ProviderId,
            ct);

        if (user is not null)
        {
            return await SignInExistingUserAsync(user, request.IpAddress, ct);
        }

        var hasExternalEmail =
            !string.IsNullOrWhiteSpace(socialUser.Email);

        var isPrivateRelay = socialUser.Email?.EndsWith(
            "@privaterelay.appleid.com",
            StringComparison.OrdinalIgnoreCase) == true;

        // A valid provider token proves control of ProviderId, but it does not
        // prove ownership of the supplied email unless email_verified is true.
        if (hasExternalEmail && !socialUser.IsEmailVerified)
        {
            _logger.LogWarning(
                "Social login rejected because the provider email is not verified. Provider={Provider}",
                providerName);

            return SocialLoginResult.Failed(
                "The social provider did not verify the supplied email address.");
        }

        // Automatic email-based linking is deliberately disabled for Apple
        // private relay addresses. ProviderId login still works above.
        if (hasExternalEmail && !isPrivateRelay)
        {
            var normalizedEmail = NormalizeEmail(socialUser.Email!);

            user = await _identityUsers.FindByEmailAsync(
                normalizedEmail,
                ct);

            if (user is not null)
            {
                if (user.IsDeleted)
                {
                    return SocialLoginResult.Failed(
                        "The account is not available.");
                }

                // Prevent account pre-hijacking: an attacker may have created
                // a local account with the victim's email without confirming it.
                if (!user.EmailConfirmed)
                {
                    _logger.LogWarning(
                        "Automatic social linking rejected because the local email is not confirmed. Provider={Provider}, UserId={UserId}",
                        providerName,
                        user.Id);

                    return SocialLoginResult.Failed(
                        "An account already uses this email. Sign in to that account and link the social provider from account settings.");
                }

                var linkResult = await _identityUsers.AddLoginAsync(
                    user,
                    providerName,
                    socialUser.ProviderId,
                    providerName,
                    ct);

                if (!linkResult.Succeeded)
                {
                    _logger.LogWarning(
                        "Failed linking {Provider} login to user {UserId}. Errors: {Errors}",
                        providerName,
                        user.Id,
                        string.Join(", ", linkResult.Errors));

                    return SocialLoginResult.Failed(
                        "Could not link the social login to the existing account.");
                }

                return await SignInExistingUserAsync(
                    user,
                    request.IpAddress,
                    ct);
            }
        }

        user = await CreateUserAsync(socialUser, request, ct);
        if (user is null)
        {
            return SocialLoginResult.Failed("Could not create the social login account.");
        }

        var addLoginResult = await _identityUsers.AddLoginAsync(
            user,
            providerName,
            socialUser.ProviderId,
            providerName,
            ct);

        if (!addLoginResult.Succeeded)
        {
            _logger.LogWarning(
                "Failed adding {Provider} login to new user {UserId}. Errors: {Errors}",
                providerName,
                user.Id,
                string.Join(", ", addLoginResult.Errors));

            return SocialLoginResult.Failed("Could not link the social login to the created account.");
        }

        var roleResult = await _identityUsers.AddToRoleAsync(user, RoleNames.User, ct);
        if (!roleResult.Succeeded)
        {
            _logger.LogWarning(
                "Failed assigning default role to social user {UserId}. Errors: {Errors}",
                user.Id,
                string.Join(", ", roleResult.Errors));

            return SocialLoginResult.Failed("Could not assign the default user role.");
        }

        return await SignInExistingUserAsync(user, request.IpAddress, ct);
    }

    private async Task<User?> CreateUserAsync(
        SocialUserInfo socialUser,
        SocialLoginCommand request,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(socialUser.Email) &&
            !socialUser.IsEmailVerified)
        {
            _logger.LogWarning(
                "Social user creation rejected because provider email is not verified. Provider={Provider}",
                socialUser.ProviderName);

            return null;
        }

        var now = DateTime.UtcNow;
        var email = !string.IsNullOrWhiteSpace(socialUser.Email)
            ? NormalizeEmail(socialUser.Email)
            : $"social_{socialUser.ProviderName.ToLowerInvariant()}_{socialUser.ProviderId}@noemail.local";

        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = socialUser.IsEmailVerified,
            FirstName = FirstNonEmpty(socialUser.FirstName, request.AppleFirstName, "User"),
            LastName = FirstNonEmpty(socialUser.LastName, request.AppleLastName, "Social"),
            ProfileImageUrl = socialUser.AvatarUrl,
            CreatedAt = now,
            UpdatedAt = now,
            LastLoginAt = now
        };

        var createResult = await _identityUsers.CreateAsync(user, ct);
        if (createResult.Succeeded)
        {
            return user;
        }

        _logger.LogWarning(
            "Failed creating social user for provider {Provider}. Errors: {Errors}",
            socialUser.ProviderName,
            string.Join(", ", createResult.Errors));

        return null;
    }

    private async Task<SocialLoginResult> SignInExistingUserAsync(
        User user,
        string? ipAddress,
        CancellationToken ct)
    {
        if (user.IsDeleted)
        {
            return SocialLoginResult.Failed("The account is not available.");
        }

        var now = DateTime.UtcNow;
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        await _identityUsers.UpdateAsync(user, ct);

        var roles = await _identityUsers.GetRolesAsync(user, ct);
        var accessToken = _tokenService.GenerateAccessToken(user, roles);
        var refreshToken = _tokenService.GenerateRefreshToken();

        await _refreshTokens.AddAsync(user.Id, refreshToken, ipAddress, ct);

        await _auditLogs.LogAsync(
            userId: user.Id,
            action: AuditActions.Login,
            ipAddress: ipAddress,
            oldValue: null,
            newValue: JsonSerializer.Serialize(new
            {
                userId = user.Id,
                email = user.Email,
                provider = "Social",
                success = true,
                timestamp = DateTime.UtcNow
            }),
            ct: ct);

        return SocialLoginResult.Ok(
            accessToken,
            refreshToken,
            _jwtSettings.AccessTokenMinutes * 60);
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
