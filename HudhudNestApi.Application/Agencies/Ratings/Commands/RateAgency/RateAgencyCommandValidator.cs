using FluentValidation;
using HudhudNestApi.Application.Common.Validation;
using HudhudNestApi.Domain.Agencies.Entities;

namespace HudhudNestApi.Application.Agencies.Ratings.Commands.RateAgency;

public sealed class RateAgencyCommandValidator : AbstractValidator<RateAgencyCommand>
{
    public RateAgencyCommandValidator()
    {
        RuleFor(x => x.AgencyId).NotEmpty();
        RuleFor(x => x.RaterId).NotEmpty();

        RuleFor(x => x.Service).InclusiveBetween(1, 5).WithMessage("تقييم جودة الخدمة يجب أن يكون بين 1 و5.");
        RuleFor(x => x.Professionalism).InclusiveBetween(1, 5).WithMessage("تقييم الاحترافية يجب أن يكون بين 1 و5.");
        RuleFor(x => x.Reliability).InclusiveBetween(1, 5).WithMessage("تقييم الموثوقية يجب أن يكون بين 1 و5.");
        RuleFor(x => x.OverallExperience).InclusiveBetween(1, 5).WithMessage("تقييم التجربة العامة يجب أن يكون بين 1 و5.");

        RuleFor(x => x.Comment)
            .MaximumLength(AgencyRating.CommentMaxLength)
            .NoNulCharacters()
            .When(x => x.Comment is not null);
    }
}
