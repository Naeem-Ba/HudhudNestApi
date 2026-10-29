using FluentValidation;
using HudhudNestApi.Application.Properties.Validators;

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

        // QA Phase 9 (P9-D07): NUL in a text parameter makes PostgreSQL fail with a 500 on this anonymous endpoint.
        RuleFor(x => x.Filter.City)
            .MaximumLength(100)
            .Must(ListingSearchText.HasNoControlCharacters)
            .WithMessage("City contains an invalid character.")
            .When(x => x.Filter.City is not null);

        // QA Phase 9 (P9-D10): impossible or one-sided dates used to be ignored silently, so a search for
        // 2020 or a reversed range returned every listing as if it were free. A date search needs both
        // ends, the end after the start, a start that is not in the past (yesterday is tolerated for
        // callers in time zones ahead of UTC) and a range of at most one year, like a booking.
        RuleFor(x => x.Filter)
            .Must(filter => filter.CheckIn.HasValue == filter.CheckOut.HasValue)
            .WithMessage("CheckIn and CheckOut must be given together.")
            .WithName("Filter.CheckOut");

        RuleFor(x => x.Filter)
            .Must(filter => !filter.CheckIn.HasValue || !filter.CheckOut.HasValue || filter.CheckOut > filter.CheckIn)
            .WithMessage("CheckOut must be after CheckIn.")
            .WithName("Filter.CheckOut");

        RuleFor(x => x.Filter)
            .Must(filter => !filter.CheckIn.HasValue || filter.CheckIn >= DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1))
            .WithMessage("CheckIn cannot be in the past.")
            .WithName("Filter.CheckIn");

        RuleFor(x => x.Filter)
            .Must(filter => !filter.CheckIn.HasValue || !filter.CheckOut.HasValue
                || filter.CheckOut.Value.DayNumber - filter.CheckIn.Value.DayNumber <= 366)
            .WithMessage("The date range cannot exceed 366 nights.")
            .WithName("Filter.CheckOut");

        RuleFor(x => x.Filter)
            .Must(filter =>
                !filter.MinPrice.HasValue ||
                !filter.MaxPrice.HasValue ||
                filter.MinPrice <= filter.MaxPrice)
            .WithMessage("MinPrice must be less than or equal to MaxPrice.");
    }
}
