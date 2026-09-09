using FluentValidation;

namespace PropertyApi.Application.SocialDistribution.Commands.CreateSocialAccount;

public sealed class CreateSocialAccountCommandValidator : AbstractValidator<CreateSocialAccountCommand>
{
    public CreateSocialAccountCommandValidator()
    {
        RuleFor(x => x.SocialChannelId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ExternalAccountId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AccountType).IsInEnum();
        RuleFor(x => x.GovernorateId).GreaterThan(0).When(x => x.GovernorateId is not null);
    }
}
