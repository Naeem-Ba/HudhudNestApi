using FluentValidation;

namespace HudhudNestApi.Application.ShortStay.Queries.SearchShortStayListings;

public sealed class SearchShortStayListingsQueryValidator : AbstractValidator<SearchShortStayListingsQuery>
{
    public SearchShortStayListingsQueryValidator()
    {
        RuleFor(x => x.Filter.Page).GreaterThan(0);
        RuleFor(x => x.Filter.PageSize).InclusiveBetween(1, 100);

        RuleFor(x => x.Filter.MinPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Filter.MinPrice.HasValue);

        RuleFor(x => x.Filter.MaxPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Filter.MaxPrice.HasValue);

        RuleFor(x => x.Filter)
            .Must(filter =>
                !filter.MinPrice.HasValue ||
                !filter.MaxPrice.HasValue ||
                filter.MinPrice <= filter.MaxPrice)
            .WithMessage("MinPrice must be less than or equal to MaxPrice.");
    }
}
