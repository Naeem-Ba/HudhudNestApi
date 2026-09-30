using FluentValidation;

namespace HudhudNestApi.Application.Services.Commands.AddServiceReview;

public sealed class AddServiceReviewCommandValidator : AbstractValidator<AddServiceReviewCommand>
{
    public AddServiceReviewCommandValidator()
    {
        RuleFor(x => x.ServiceRequestId).NotEmpty();
        RuleFor(x => x.ReviewerId).NotEmpty();

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5)
            .WithMessage("التقييم يجب أن يكون بين 1 و5.");

        RuleFor(x => x.Comment)
            .MaximumLength(2000)
            .When(x => x.Comment is not null);
    }
}
