using FluentValidation;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Properties.Validators;

public sealed class PropertyFilterDtoValidator
    : AbstractValidator<PropertyFilterDto>
{
    private static readonly string[] AllowedSortColumns =
    [
        "CreatedAt",
        "PurchasePrice",
        "ColdRent",
        "Area",
        // Cheapest/most expensive across currencies; needs a sale or rent type (see rule below).
        "Price"
    ];

    /// <summary>
    /// Deepest row the public list will page to (page x page size). Offset paging reads and
    /// discards every skipped row, so an unbounded page number is both a cost lever for abuse
    /// and, at int.MaxValue, an arithmetic overflow that used to answer 500 (QA Phase 7, P7-D03).
    /// Sites such as Google and Zillow cap result depth the same way; narrowing the filters is
    /// the way to reach row 10,001.
    /// </summary>
    public const int MaxResultDepth = 10_000;

    public PropertyFilterDtoValidator()
    {
        RuleFor(filter => filter.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(filter => filter.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(filter => filter)
            .Must(filter => (long)filter.Page * filter.PageSize <= MaxResultDepth)
            .OverridePropertyName("Page")
            .WithMessage($"Page is beyond the first {MaxResultDepth} results; narrow the search instead.")
            .When(filter => filter.Page >= 1 && filter.PageSize is >= 1 and <= 100);

        RuleFor(filter => filter.SearchTerm)
            .MaximumLength(200)
            .When(filter =>
                !string.IsNullOrWhiteSpace(filter.SearchTerm));

        RuleFor(filter => filter.SearchTerm)
            .Must(ListingSearchText.HasNoControlCharacters)
            .WithMessage("SearchTerm contains characters that are not allowed.");

        RuleFor(filter => filter.City)
            .Must(ListingSearchText.HasNoControlCharacters)
            .WithMessage("City contains characters that are not allowed.");

        RuleFor(filter => filter.Region)
            .Must(ListingSearchText.HasNoControlCharacters)
            .WithMessage("Region contains characters that are not allowed.");

        RuleFor(filter => filter.CountryCode)
            .Length(2)
            .When(filter =>
                !string.IsNullOrWhiteSpace(filter.CountryCode));

        RuleFor(filter => filter.CurrencyCode)
            .Length(3)
            .When(filter =>
                !string.IsNullOrWhiteSpace(filter.CurrencyCode));

        RuleFor(filter => filter.CurrencyCode)
            .Must(SearchCurrencies.IsSupported)
            .WithMessage(SearchCurrencies.UnsupportedMessage)
            .When(filter =>
                !string.IsNullOrWhiteSpace(filter.CurrencyCode) && filter.CurrencyCode!.Trim().Length == 3);

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

        RuleFor(filter => filter)
            .Must(filter => !filter.MinRooms.HasValue ||
                            !filter.MaxRooms.HasValue ||
                            filter.MinRooms <= filter.MaxRooms)
            .OverridePropertyName("MinRooms")
            .WithMessage("MinRooms must be less than or equal to MaxRooms.");

        RuleFor(filter => filter)
            .Must(filter => !filter.MinArea.HasValue ||
                            !filter.MaxArea.HasValue ||
                            filter.MinArea <= filter.MaxArea)
            .OverridePropertyName("MinArea")
            .WithMessage("MinArea must be less than or equal to MaxArea.");

        // Sale prices and monthly rents are different quantities; ordering them in one list put
        // every sale first (and, descending, the listings with no price before everything) —
        // QA Phase 7, P7-D02. Browsing prices needs a side of the market, like every portal.
        RuleFor(filter => filter)
            .Must(filter => filter.ListingType is ListingType.ForSale or ListingType.ForRent)
            .OverridePropertyName("SortBy")
            .WithMessage("SortBy=Price requires ListingType ForSale or ForRent.")
            .When(filter => string.Equals(filter.SortBy, "Price", StringComparison.OrdinalIgnoreCase));

        RuleFor(filter => filter.SortBy)
            .Must(sortBy => AllowedSortColumns.Contains(
                sortBy,
                StringComparer.OrdinalIgnoreCase))
            .WithMessage("SortBy is not supported.")
            .When(filter => !string.IsNullOrWhiteSpace(filter.SortBy));
    }
}
