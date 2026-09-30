using FluentValidation;

namespace HudhudNestApi.Application.Reviews.Commands.AddReview;

public sealed class AddReviewCommandValidator : AbstractValidator<AddReviewCommand>
{
    public AddReviewCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.ReviewerId).NotEmpty();
        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5)
            .WithMessage("Ø§Ù„ØªÙ‚ÙŠÙŠÙ… ÙŠØ¬Ø¨ Ø£Ù† ÙŠÙƒÙˆÙ† Ø¨ÙŠÙ† 1 Ùˆ5 Ù†Ø¬ÙˆÙ….");
        RuleFor(x => x.Comment)
            .MaximumLength(1000)
            .When(x => x.Comment is not null);
    }
}
