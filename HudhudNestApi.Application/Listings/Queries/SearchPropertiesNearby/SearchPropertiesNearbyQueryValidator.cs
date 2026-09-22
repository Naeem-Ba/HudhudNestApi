using FluentValidation;

namespace HudhudNestApi.Application.Listings.Queries.SearchPropertiesNearby;

public sealed class SearchPropertiesNearbyQueryValidator
    : AbstractValidator<SearchPropertiesNearbyQuery>
{
    public SearchPropertiesNearbyQueryValidator()
    {
        RuleFor(x => x.Filter).NotNull();

        When(x => x.Filter is not null, () =>
        {
            RuleFor(x => x.Filter.Latitude)
                .InclusiveBetween(-90m, 90m)
                .WithMessage("Latitude must be between -90 and 90.");

            RuleFor(x => x.Filter.Longitude)
                .InclusiveBetween(-180m, 180m)
                .WithMessage("Longitude must be between -180 and 180.");

            RuleFor(x => x.Filter.RadiusKm)
                .GreaterThan(0m)
                .LessThanOrEqualTo(50m)
                .WithMessage("RadiusKm must be greater than 0 and less than or equal to 50.");

            RuleFor(x => x.Filter.Page)
                .GreaterThanOrEqualTo(1);

            RuleFor(x => x.Filter.PageSize)
                .InclusiveBetween(1, 100);

            RuleFor(x => x.Filter)
                .Must(filter => filter.Page * filter.PageSize <= 1000)
                .WithMessage("Geo search cannot request more than 1000 skipped/returned rows. Reduce Page or PageSize.");

            RuleFor(x => x.Filter.CountryCode)
                .Length(2)
                .When(x => !string.IsNullOrWhiteSpace(x.Filter.CountryCode));

            RuleFor(x => x.Filter.CurrencyCode)
                .Length(3)
                .When(x => !string.IsNullOrWhiteSpace(x.Filter.CurrencyCode));

            RuleFor(x => x.Filter.MinPrice)
                .GreaterThanOrEqualTo(0)
                .When(x => x.Filter.MinPrice.HasValue);

            RuleFor(x => x.Filter.MaxPrice)
                .GreaterThanOrEqualTo(0)
                .When(x => x.Filter.MaxPrice.HasValue);

            RuleFor(x => x.Filter)
                .Must(filter => !filter.MinPrice.HasValue ||
                                !filter.MaxPrice.HasValue ||
                                filter.MinPrice <= filter.MaxPrice)
                .WithMessage("MinPrice must be less than or equal to MaxPrice.");
        });
    }
}

