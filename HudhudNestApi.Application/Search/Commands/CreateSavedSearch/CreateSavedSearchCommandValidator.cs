using FluentValidation;
using HudhudNestApi.Application.Listings.Currencies;

namespace HudhudNestApi.Application.Search.Commands.CreateSavedSearch;

public sealed class CreateSavedSearchCommandValidator : AbstractValidator<CreateSavedSearchCommand>
{
    public CreateSavedSearchCommandValidator()
    {
        // A saved search with no criterion matches every new listing, so the background matcher
        // would notify its owner about each one (capped at 10 per run, but every run). A name or a
        // currency alone is not a criterion.
        RuleFor(x => x)
            .Must(HasAtLeastOneCriterion)
            .OverridePropertyName("Criteria")
            .WithErrorCode("SavedSearchCriteriaRequired")
            .WithMessage("يجب تحديد معيار واحد على الأقل للبحث المحفوظ.");

        RuleFor(x => x.CurrencyCode)
            .Must(SearchCurrencies.IsSupported)
            .WithMessage(SearchCurrencies.UnsupportedMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.CurrencyCode));
    }

    private static bool HasAtLeastOneCriterion(CreateSavedSearchCommand c) =>
        !string.IsNullOrWhiteSpace(c.CountryCode)
        || !string.IsNullOrWhiteSpace(c.City)
        || !string.IsNullOrWhiteSpace(c.Region)
        || c.GovernorateId.HasValue
        || c.DistrictId.HasValue
        || c.NeighborhoodId.HasValue
        || c.PropertyTypeId.HasValue
        || c.ListingType.HasValue
        || c.MinPrice.HasValue
        || c.MaxPrice.HasValue
        || c.MinRooms.HasValue
        || c.MaxRooms.HasValue
        || c.MinArea.HasValue
        || c.MaxArea.HasValue;
}
