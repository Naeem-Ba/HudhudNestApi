using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Abstractions;
using PropertyApi.Application.Auth.Commands.SocialLogin;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Auth.Policies;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Observability;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Orchestration;

public sealed class SocialAccountMutationCoordinator
{
    private readonly ISocialLoginIdentityService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly IAuthenticationSessionIssuer _sessions;
    private readonly SocialAccountCreationPolicy _creationPolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SocialAccountMutationCoordinator> _logger;

    public SocialAccountMutationCoordinator(
        ISocialLoginIdentityService identity,
        IUserAccountRepository accounts,
        IAuthenticationSessionIssuer sessions,
        SocialAccountCreationPolicy creationPolicy,
        IUnitOfWork unitOfWork,
        ILogger<SocialAccountMutationCoordinator> logger)
    {
        _identity = identity;
        _accounts = accounts;
        _sessions = sessions;
        _creationPolicy = creationPolicy;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task<SocialLoginResult> CompleteAsync(
        SocialAccountResolution resolution,
        SocialLoginCommand command,
        CancellationToken cancellationToken) =>
        resolution.Kind switch
        {
            SocialAccountResolutionKind.ExistingLinkedAccount =>
                IssueSessionAsync(resolution.Account!, command.IpAddress, cancellationToken),
            SocialAccountResolutionKind.ExistingUnlinkedAccount =>
                LinkExistingAsync(resolution, command.IpAddress, cancellationToken),
            SocialAccountResolutionKind.NoAccountFound =>
                CreateNewAsync(resolution.VerifiedIdentity, command, cancellationToken),
            _ => Task.FromResult(SocialLoginResult.Failed(
                resolution.ErrorMessage ?? "The social account could not be resolved."))
        };

    private async Task<SocialLoginResult> LinkExistingAsync(
        SocialAccountResolution resolution,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var verified = resolution.VerifiedIdentity;
            var account = resolution.Account!;
            var linked = await _identity.AddLoginAsync(
                account.IdentityId,
                verified.Provider,
                verified.User.ProviderId,
                verified.Provider,
                cancellationToken);
            if (!linked.Succeeded)
            {
                ApplicationTelemetry.RecordAuthenticationStage("account_linking", "failed", "social");
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return SocialLoginResult.Failed(
                    "Could not link the social login to the existing account.");
            }

            var session = await IssueSessionAsync(account, ipAddress, cancellationToken);
            if (!session.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return session;
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            ApplicationTelemetry.RecordAuthenticationStage("account_linking", "success", "social");
            return session;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SocialLoginResult> CreateNewAsync(
        VerifiedSocialIdentity verified,
        SocialLoginCommand command,
        CancellationToken cancellationToken)
    {
        if (_creationPolicy.Evaluate(verified.User) == AccountCreationDecision.Forbidden)
        {
            return SocialLoginResult.Failed(
                "The social provider did not verify the supplied email address.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var created = await CreateIdentityAsync(verified, command, cancellationToken);
            if (created is null)
            {
                ApplicationTelemetry.RecordAuthenticationStage("account_creation", "failed", "social");
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return SocialLoginResult.Failed("Could not create the social login account.");
            }

            var linked = await _identity.AddLoginAsync(
                created.Identity.IdentityId,
                verified.Provider,
                verified.User.ProviderId,
                verified.Provider,
                cancellationToken);
            var role = linked.Succeeded
                ? await _identity.AddToRoleAsync(
                    created.Identity.IdentityId,
                    RoleNames.User,
                    cancellationToken)
                : IdentityOperationResult.Failed("EXTERNAL_LOGIN_LINK_FAILED");
            if (!linked.Succeeded || !role.Succeeded)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return SocialLoginResult.Failed("Could not initialize the social login account.");
            }

            await _accounts.AddAsync(
                UserAccount.Create(
                    created.Identity.IdentityId,
                    created.FirstName,
                    created.LastName,
                    created.CreatedAtUtc),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var session = await IssueSessionAsync(
                created.Identity,
                command.IpAddress,
                cancellationToken);
            if (!session.Success)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return session;
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            ApplicationTelemetry.RecordAuthenticationStage("account_creation", "success", "social");
            return session;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<CreatedSocialIdentity?> CreateIdentityAsync(
        VerifiedSocialIdentity verified,
        SocialLoginCommand command,
        CancellationToken cancellationToken)
    {
        var user = verified.User;
        var now = DateTime.UtcNow;
        var identityId = Guid.NewGuid();
        var email = !string.IsNullOrWhiteSpace(user.Email)
            ? user.Email.Trim().ToLowerInvariant()
            : $"social_{verified.Provider.ToLowerInvariant()}_{user.ProviderId}@noemail.local";
        var firstName = FirstNonEmpty(user.FirstName, command.AppleFirstName, "User");
        var lastName = FirstNonEmpty(user.LastName, command.AppleLastName, "Social");
        var result = await _identity.CreateAsync(
            new CreateIdentityAccount(
                identityId,
                email,
                null,
                null,
                firstName,
                lastName,
                now,
                user.IsEmailVerified,
                user.AvatarUrl),
            cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Social identity creation failed. Provider={Provider}, ErrorCount={ErrorCount}",
                verified.Provider,
                result.Errors.Count);
            return null;
        }

        var identity = await _identity.FindByIdAsync(identityId, cancellationToken);
        return identity is null
            ? null
            : new(identity, firstName, lastName, now);
    }

    private async Task<SocialLoginResult> IssueSessionAsync(
        IdentityAccountSnapshot identity,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var session = await _sessions.IssueAsync(
            new AuthenticationSessionRequest(
                identity,
                "social",
                ipAddress,
                SuccessfulLoginRecordingMode.Required),
            cancellationToken);
        return session.Succeeded
            ? SocialLoginResult.Ok(
                session.AccessToken!,
                session.RefreshToken!,
                session.ExpiresInSeconds)
            : SocialLoginResult.Failed("Could not issue the authentication session.");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private sealed record CreatedSocialIdentity(
        IdentityAccountSnapshot Identity,
        string FirstName,
        string LastName,
        DateTime CreatedAtUtc);
}
