using FluentValidation;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ConnectSocialAccount;

public sealed class ConnectSocialAccountCommandValidator : AbstractValidator<ConnectSocialAccountCommand>
{
    public ConnectSocialAccountCommandValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        // Phase 14 (D12): an account "connected" with no credential showed as Active but could never publish.
        RuleFor(x => x.CredentialReference).NotEmpty().MaximumLength(500);
    }
}
