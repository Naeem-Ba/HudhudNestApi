using FluentValidation;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Properties.Validators;

public sealed class PropertyFilterDtoValidator
    : AbstractValidator<PropertyFilterDto>
{
    private static readonly string[] AllowedSortColumns =
    [
        "CreatedAt",
        "PurchasePrice",
        "ColdRent",
        "Area"
    ];

    public PropertyFilterDtoValidator()
    {
        RuleFor(filter => filter.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(filter => filter.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(filter => filter.SearchTerm)
            .MaximumLength(200)
            .When(filter =>
                !string.IsNullOrWhiteSpace(filter.SearchTerm));

        RuleFor(filter => filter.CountryCode)
            .Length(2)
            .When(filter =>
                !string.IsNullOrWhiteSpace(filter.CountryCode));

        RuleFor(filter => filter.CurrencyCode)
            .Length(3)
            .When(filter =>
                !string.IsNullOrWhiteSpace(filter.CurrencyCode));

        RuleFor(filter => filter.MinPrice)
            .GreaterThanOrEqualTo(0)
            .When(filter => filter.MinPrice.HasValue);

        RuleFor(filter => filter.MaxPrice)
            .GreaterThanOrEqualTo(0)
            .When(filter => filter.MaxPrice.HasValue);

        RuleFor(filter => filter)
            .Must(filter =>
                !filter.MinPrice.HasValue ||
                !filter.MaxPrice.HasValue ||
                filter.MinPrice <= filter.MaxPrice)
            .WithMessage(
                "MinPrice must be less than or equal to MaxPrice.");

        RuleFor(filter => filter.SortBy)
            .Must(sortBy => AllowedSortColumns.Contains(
                sortBy,
                StringComparer.OrdinalIgnoreCase))
            .WithMessage("SortBy is not supported.")
            .When(filter => !string.IsNullOrWhiteSpace(filter.SortBy));
    }
}
