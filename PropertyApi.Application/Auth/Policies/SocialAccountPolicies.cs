using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Policies;

public sealed class SocialAccountLinkingPolicy
{
    public AccountLinkingDecision Evaluate(
        SocialUserInfo providerIdentity,
        IdentityAccountSnapshot localAccount)
    {
        if (providerIdentity.Email is null ||
            !providerIdentity.IsEmailVerified ||
            providerIdentity.Email.EndsWith(
                "@privaterelay.appleid.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return AccountLinkingDecision.Forbidden;
        }

        return localAccount.EmailConfirmed && !localAccount.IsDeleted
            ? AccountLinkingDecision.Allowed
            : AccountLinkingDecision.AdditionalVerificationRequired;
    }
}

public sealed class SocialAccountCreationPolicy
{
    public AccountCreationDecision Evaluate(SocialUserInfo providerIdentity) =>
        string.IsNullOrWhiteSpace(providerIdentity.Email) ||
        providerIdentity.IsEmailVerified
            ? AccountCreationDecision.Allowed
            : AccountCreationDecision.Forbidden;
}
