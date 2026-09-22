using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Auth.Policies;

public enum PhoneOwnershipDecision
{
    Available,
    OwnedByResolvedUser,
    Forbidden
}

public sealed class PhoneOwnershipPolicy
{
    public PhoneOwnershipDecision Evaluate(IdentityAccountSnapshot? account) =>
        account is null
            ? PhoneOwnershipDecision.Available
            : account.IsDeleted
                ? PhoneOwnershipDecision.Forbidden
                : PhoneOwnershipDecision.OwnedByResolvedUser;
}
