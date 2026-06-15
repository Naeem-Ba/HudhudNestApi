using FluentValidation;

namespace PropertyApi.Application.Reviews.Commands.AddReview;

public sealed class AddReviewCommandValidator : AbstractValidator<AddReviewCommand>
{
    public AddReviewCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.ReviewerId).NotEmpty();
        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5)
            .WithMessage("التقييم يجب أن يكون بين 1 و5 نجوم.");
        RuleFor(x => x.Comment)
            .MaximumLength(1000)
            .When(x => x.Comment is not null);
    }
}