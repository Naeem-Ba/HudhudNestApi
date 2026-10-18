using FluentValidation;
using HudhudNestApi.Application.Common.Validation;

namespace HudhudNestApi.Application.ShortStay.Commands.AddShortStayReview;

/// <summary>
/// QA Phase 9 (P9-D05): the comment had no length check, so a long one reached the database and came back
/// as HTTP 500. The limit matches the other review systems (property reviews, user ratings).
/// </summary>
public sealed class AddShortStayReviewCommandValidator : AbstractValidator<AddShortStayReviewCommand>
{
    public const int MaxCommentLength = 1000;

    public AddShortStayReviewCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.ReviewerId).NotEmpty();

        RuleFor(x => x.Rating).InclusiveBetween(1, 5);

        RuleFor(x => x.Comment)
            .MaximumLength(MaxCommentLength)
            .NoNulCharacters()
            .When(x => x.Comment is not null);
    }
}
