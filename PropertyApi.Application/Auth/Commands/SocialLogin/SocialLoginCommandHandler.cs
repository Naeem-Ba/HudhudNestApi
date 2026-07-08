using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.SocialLogin;

public sealed class SocialLoginCommandHandler
    : IRequestHandler<SocialLoginCommand, SocialLoginResult>
{
    private readonly IEnumerable<ISocialTokenVerifier> _verifiers;
    private readonly IPureIdentityService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtTokenSettings _jwtSettings;
    private readonly IAuditLogService _auditLogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SocialLoginCommandHandler> _logger;

    public SocialLoginCommandHandler(
        IEnumerable<ISocialTokenVerifier> verifiers,
        IPureIdentityService identity,
        IUserAccountRepository accounts,
        ITokenService tokenService,
        IRefreshTokenRepository refreshTokens,
        IJwtTokenSettings jwtSettings,
        IAuditLogService auditLogs,
        IUnitOfWork unitOfWork,
        ILogger<SocialLoginCommandHandler> logger)
    {
        _verifiers = verifiers;
        _identity = identity;
        _accounts = accounts;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _jwtSettings = jwtSettings;
        _auditLogs = auditLogs;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<SocialLoginResult> Handle(
        SocialLoginCommand request,
        CancellationToken ct)
    {
        var providerName =
            !string.IsNullOrWhiteSpace(
                request.GoogleIdToken)
                ? "Google"
                : !string.IsNullOrWhiteSpace(
                    request.AppleIdentityToken)
                    ? "Apple"
                    : null;

        var rawToken =
            providerName switch
            {
                "Google" =>
                    request.GoogleIdToken,

                "Apple" =>
                    request.AppleIdentityToken,

                _ =>
                    null
            };

        if (string.IsNullOrWhiteSpace(providerName) ||
            string.IsNullOrWhiteSpace(rawToken))
        {
            return SocialLoginResult.Failed(
                "A Google or Apple identity token is required.");
        }

        var verifier =
            _verifiers.FirstOrDefault(
                candidate =>
                    candidate.ProviderName.Equals(
                        providerName,
                        StringComparison.OrdinalIgnoreCase));

        if (verifier is null)
        {
            _logger.LogError(
                "No social token verifier is registered for provider {Provider}.",
                providerName);

            return SocialLoginResult.Failed(
                "Social login is not configured for this provider.");
        }

        var socialUser =
            await verifier.VerifyAsync(
                rawToken,
                ct);

        if (socialUser is null)
        {
            _logger.LogWarning(
                "Social token verification failed for provider {Provider}.",
                providerName);

            return SocialLoginResult.Failed(
                "The social identity token is invalid or expired.");
        }

        var identity =
            await _identity.FindByLoginAsync(
                providerName,
                socialUser.ProviderId,
                ct);

        /*
         * Provider identity already linked.
         *
         * No linking transaction is required.
         */
        if (identity is not null)
        {
            return await SignInExistingIdentityAsync(
                identity,
                request.IpAddress,
                ct);
        }

        var hasExternalEmail =
            !string.IsNullOrWhiteSpace(
                socialUser.Email);

        var isPrivateRelay =
            socialUser.Email?.EndsWith(
                "@privaterelay.appleid.com",
                StringComparison.OrdinalIgnoreCase)
            == true;

        /*
         * ProviderId ownership does not automatically prove
         * ownership of the external email.
         *
         * Email-based linking is permitted only when the
         * provider explicitly confirms the email.
         */
        if (hasExternalEmail &&
            !socialUser.IsEmailVerified)
        {
            _logger.LogWarning(
                "Social login rejected because the provider email is not verified. Provider={Provider}",
                providerName);

            return SocialLoginResult.Failed(
                "The social provider did not verify the supplied email address.");
        }

        /*
         * Apple private relay addresses must not be used for
         * automatic email-based account linking.
         */
        if (hasExternalEmail &&
            !isPrivateRelay)
        {
            var normalizedEmail =
                NormalizeEmail(
                    socialUser.Email!);

            identity =
                await _identity.FindByEmailAsync(
                    normalizedEmail,
                    ct);

            if (identity is not null)
            {
                if (identity.IsDeleted)
                {
                    return SocialLoginResult.Failed(
                        "The account is not available.");
                }

                /*
                 * Account pre-hijacking protection:
                 *
                 * Never automatically link a social identity
                 * to a local account whose email has not been
                 * confirmed.
                 */
                if (!identity.EmailConfirmed)
                {
                    _logger.LogWarning(
                        "Automatic social linking rejected because the local email is not confirmed. Provider={Provider}, UserId={UserId}",
                        providerName,
                        identity.IdentityId);

                    return SocialLoginResult.Failed(
                        "An account already uses this email. " +
                        "Sign in to that account and link the social provider " +
                        "from account settings.");
                }

                return await LinkAndSignInExistingIdentityAsync(
                    identity,
                    providerName,
                    socialUser.ProviderId,
                    request.IpAddress,
                    ct);
            }
        }

        return await CreateAndSignInNewSocialIdentityAsync(
            socialUser,
            request,
            providerName,
            ct);
    }

    private async Task<SocialLoginResult>
        LinkAndSignInExistingIdentityAsync(
            IdentityAccountSnapshot identity,
            string providerName,
            string providerId,
            string? ipAddress,
            CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(
            ct);

        try
        {
            var linkResult =
                await _identity.AddLoginAsync(
                    identity.IdentityId,
                    providerName,
                    providerId,
                    providerName,
                    ct);

            if (!linkResult.Succeeded)
            {
                _logger.LogWarning(
                    "Failed linking {Provider} login to identity {IdentityId}. Errors: {Errors}",
                    providerName,
                    identity.IdentityId,
                    string.Join(
                        ", ",
                        linkResult.Errors));

                await _unitOfWork.RollbackTransactionAsync(
                    ct);

                return SocialLoginResult.Failed(
                    "Could not link the social login to the existing account.");
            }

            var signInResult =
                await SignInExistingIdentityAsync(
                    identity,
                    ipAddress,
                    ct);

            if (!signInResult.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(
                    ct);

                return signInResult;
            }

            await _unitOfWork.CommitTransactionAsync(
                ct);

            return signInResult;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);

            throw;
        }
    }

    private async Task<SocialLoginResult>
        CreateAndSignInNewSocialIdentityAsync(
            SocialUserInfo socialUser,
            SocialLoginCommand request,
            string providerName,
            CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(
            ct);

        try
        {
            var created =
                await CreateIdentityAsync(
                    socialUser,
                    request,
                    ct);

            if (created is null)
            {
                await _unitOfWork.RollbackTransactionAsync(
                    ct);

                return SocialLoginResult.Failed(
                    "Could not create the social login account.");
            }

            var addLoginResult =
                await _identity.AddLoginAsync(
                    created.Identity.IdentityId,
                    providerName,
                    socialUser.ProviderId,
                    providerName,
                    ct);

            if (!addLoginResult.Succeeded)
            {
                _logger.LogWarning(
                    "Failed adding {Provider} login to new identity {IdentityId}. Errors: {Errors}",
                    providerName,
                    created.Identity.IdentityId,
                    string.Join(
                        ", ",
                        addLoginResult.Errors));

                await _unitOfWork.RollbackTransactionAsync(
                    ct);

                return SocialLoginResult.Failed(
                    "Could not link the social login to the created account.");
            }

            var roleResult =
                await _identity.AddToRoleAsync(
                    created.Identity.IdentityId,
                    RoleNames.User,
                    ct);

            if (!roleResult.Succeeded)
            {
                _logger.LogWarning(
                    "Failed assigning default role to social identity {IdentityId}. Errors: {Errors}",
                    created.Identity.IdentityId,
                    string.Join(
                        ", ",
                        roleResult.Errors));

                await _unitOfWork.RollbackTransactionAsync(
                    ct);

                return SocialLoginResult.Failed(
                    "Could not assign the default user role.");
            }

            var account =
                UserAccount.Create(
                    created.Identity.IdentityId,
                    created.FirstName,
                    created.LastName,
                    created.CreatedAtUtc);

            await _accounts.AddAsync(
                account,
                ct);

            await _unitOfWork.SaveChangesAsync(
                ct);

            var signInResult =
                await SignInExistingIdentityAsync(
                    created.Identity,
                    request.IpAddress,
                    ct);

            if (!signInResult.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(
                    ct);

                return signInResult;
            }

            await _unitOfWork.CommitTransactionAsync(
                ct);

            return signInResult;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);

            throw;
        }
    }

    private async Task<CreatedSocialIdentity?>
        CreateIdentityAsync(
            SocialUserInfo socialUser,
            SocialLoginCommand request,
            CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(
                socialUser.Email) &&
            !socialUser.IsEmailVerified)
        {
            _logger.LogWarning(
                "Social user creation rejected because provider email is not verified. Provider={Provider}",
                socialUser.ProviderName);

            return null;
        }

        var now =
            DateTime.UtcNow;

        var identityId =
            Guid.NewGuid();

        var email =
            !string.IsNullOrWhiteSpace(
                socialUser.Email)
                ? NormalizeEmail(
                    socialUser.Email)

                : $"social_{socialUser.ProviderName.ToLowerInvariant()}_" +
                  $"{socialUser.ProviderId}@noemail.local";

        var firstName =
            FirstNonEmpty(
                socialUser.FirstName,
                request.AppleFirstName,
                "User");

        var lastName =
            FirstNonEmpty(
                socialUser.LastName,
                request.AppleLastName,
                "Social");

        var createRequest =
            new CreateIdentityAccount(
                UserAccountId:
                    identityId,

                Email:
                    email,

                PhoneNumber:
                    null,

                Password:
                    null,

                LegacyFirstName:
                    firstName,

                LegacyLastName:
                    lastName,

                LegacyCreatedAtUtc:
                    now,

                EmailConfirmed:
                    socialUser.IsEmailVerified,

                LegacyProfileImageUrl:
                    socialUser.AvatarUrl);

        var createResult =
            await _identity.CreateAsync(
                createRequest,
                ct);

        if (!createResult.Succeeded)
        {
            _logger.LogWarning(
                "Failed creating social identity for provider {Provider}. Errors: {Errors}",
                socialUser.ProviderName,
                string.Join(
                    ", ",
                    createResult.Errors));

            return null;
        }

        /*
         * Re-read the neutral identity snapshot because token issuance
         * requires UserName and SecurityStamp.
         */
        var identity =
            await _identity.FindByIdAsync(
                identityId,
                ct);

        if (identity is null)
        {
            _logger.LogError(
                "Social identity {IdentityId} was created but could not be reloaded.",
                identityId);

            return null;
        }

        return new CreatedSocialIdentity(
            Identity:
                identity,

            FirstName:
                firstName,

            LastName:
                lastName,

            CreatedAtUtc:
                now);
    }

    private async Task<SocialLoginResult>
        SignInExistingIdentityAsync(
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

    private static string NormalizeEmail(
        string email)
        => email
            .Trim()
            .ToLowerInvariant();

    private static string FirstNonEmpty(
        params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private sealed record CreatedSocialIdentity(
        IdentityAccountSnapshot Identity,
        string FirstName,
        string LastName,
        DateTime CreatedAtUtc);
}