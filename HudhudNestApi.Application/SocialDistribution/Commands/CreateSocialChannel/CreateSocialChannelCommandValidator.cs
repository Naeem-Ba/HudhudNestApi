using FluentValidation;

namespace HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialChannel;

public sealed class CreateSocialChannelCommandValidator : AbstractValidator<CreateSocialChannelCommand>
{
    public CreateSocialChannelCommandValidator()
    {
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ConfigurationVersion).MaximumLength(50).When(x => x.ConfigurationVersion is not null);
    }
}
