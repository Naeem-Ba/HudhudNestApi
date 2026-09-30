using HudhudNestApi.Application.Auth.Contracts;

namespace HudhudNestApi.Application.Auth.Models;

public sealed record VerifiedSocialIdentity(
    string Provider,
    SocialUserInfo User);

public enum SocialAccountResolutionKind
{
    ExistingLinkedAccount,
    ExistingUnlinkedAccount,
    NoAccountFound,
    Conflict
}

public sealed record SocialAccountResolution(
    SocialAccountResolutionKind Kind,
    VerifiedSocialIdentity VerifiedIdentity,
    IdentityAccountSnapshot? Account = null,
    string? ErrorMessage = null);

public enum AccountLinkingDecision
{
    Allowed,
    AdditionalVerificationRequired,
    Forbidden
}

public enum AccountCreationDecision
{
    Allowed,
    Forbidden
}
