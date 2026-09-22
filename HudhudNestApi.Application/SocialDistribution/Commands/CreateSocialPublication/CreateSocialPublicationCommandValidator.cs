using FluentValidation;

namespace HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialPublication;

public sealed class CreateSocialPublicationCommandValidator : AbstractValidator<CreateSocialPublicationCommand>
{
    public CreateSocialPublicationCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.SocialAccountId).NotEmpty();
        RuleFor(x => x.CreatedByUserId).NotEmpty();
        RuleFor(x => x.Title).MaximumLength(300).When(x => x.Title is not null);
        RuleFor(x => x.Body).MaximumLength(10_000).When(x => x.Body is not null);
        RuleFor(x => x.ImageUrl).MaximumLength(2000).When(x => x.ImageUrl is not null);
        RuleFor(x => x.Language).NotEmpty().MaximumLength(5);
    }
}
