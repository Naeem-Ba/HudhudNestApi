using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Auth.Policies;

namespace HudhudNestApi.Application.Auth.Orchestration;

public sealed class SocialAccountResolver
{
    private readonly ISocialLoginIdentityService _identity;
    private readonly SocialAccountLinkingPolicy _linkingPolicy;

    public SocialAccountResolver(
        ISocialLoginIdentityService identity,
        SocialAccountLinkingPolicy linkingPolicy)
    {
        _identity = identity;
        _linkingPolicy = linkingPolicy;
    }

    public async Task<SocialAccountResolution> ResolveAsync(
        VerifiedSocialIdentity verified,
        CancellationToken cancellationToken)
    {
        var linked = await _identity.FindByLoginAsync(
            verified.Provider,
            verified.User.ProviderId,
            cancellationToken);
        if (linked is not null)
        {
            return linked.IsDeleted
                ? Conflict(verified, "The account is not available.")
                : new(SocialAccountResolutionKind.ExistingLinkedAccount, verified, linked);
        }

        if (!string.IsNullOrWhiteSpace(verified.User.Email) &&
            !verified.User.IsEmailVerified)
        {
            return Conflict(
                verified,
                "The social provider did not verify the supplied email address.");
        }

        if (string.IsNullOrWhiteSpace(verified.User.Email) ||
            verified.User.Email.EndsWith(
                "@privaterelay.appleid.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return new(SocialAccountResolutionKind.NoAccountFound, verified);
        }

        var account = await _identity.FindByEmailAsync(
            verified.User.Email.Trim().ToLowerInvariant(),
            cancellationToken);
        if (account is null)
        {
            return new(SocialAccountResolutionKind.NoAccountFound, verified);
        }

        return _linkingPolicy.Evaluate(verified.User, account) == AccountLinkingDecision.Allowed
            ? new(SocialAccountResolutionKind.ExistingUnlinkedAccount, verified, account)
            : Conflict(
                verified,
                "An account already uses this email. Sign in to that account and link the social provider from account settings.");
    }

    private static SocialAccountResolution Conflict(
        VerifiedSocialIdentity verified,
        string message) =>
        new(SocialAccountResolutionKind.Conflict, verified, ErrorMessage: message);
}
