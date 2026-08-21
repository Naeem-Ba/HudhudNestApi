using FluentValidation;

namespace PropertyApi.Application.Reviews.Commands.RateUser;

public sealed class RateUserCommandValidator : AbstractValidator<RateUserCommand>
{
    public RateUserCommandValidator()
    {
        RuleFor(x => x.RatedUserId).NotEmpty();
        RuleFor(x => x.RaterId).NotEmpty();

        RuleFor(x => x.Credibility)
            .InclusiveBetween(1, 5)
            .WithMessage("تقييم المصداقية يجب أن يكون بين 1 و5.");

        RuleFor(x => x.Safety)
            .InclusiveBetween(1, 5)
            .WithMessage("تقييم الأمان يجب أن يكون بين 1 و5.");

        RuleFor(x => x.ResponseSpeed)
            .InclusiveBetween(1, 5)
            .WithMessage("تقييم سرعة الرد يجب أن يكون بين 1 و5.");

        RuleFor(x => x.Transparency)
            .InclusiveBetween(1, 5)
            .WithMessage("تقييم الشفافية يجب أن يكون بين 1 و5.");

        RuleFor(x => x.Comment)
            .MaximumLength(1000)
            .When(x => x.Comment is not null);
    }
}
