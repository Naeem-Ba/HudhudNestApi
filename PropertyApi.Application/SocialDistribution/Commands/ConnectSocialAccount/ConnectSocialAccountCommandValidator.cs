using FluentValidation;

namespace PropertyApi.Application.SocialDistribution.Commands.ConnectSocialAccount;

public sealed class ConnectSocialAccountCommandValidator : AbstractValidator<ConnectSocialAccountCommand>
{
    public ConnectSocialAccountCommandValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.CredentialReference).MaximumLength(500).When(x => x.CredentialReference is not null);
    }
}
