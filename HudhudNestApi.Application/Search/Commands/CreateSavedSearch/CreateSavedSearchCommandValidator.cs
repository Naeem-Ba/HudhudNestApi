using FluentValidation;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Validation;
using HudhudNestApi.Application.Listings.Currencies;

namespace HudhudNestApi.Application.Search.Commands.CreateSavedSearch;

public sealed class CreateSavedSearchCommandValidator : AbstractValidator<CreateSavedSearchCommand>
{
    /// <summary>Largest value a rooms filter can usefully take; listings are capped at 50 rooms.</summary>
    private const int MaxRooms = 50;

    public CreateSavedSearchCommandValidator(ILocationHierarchyChecker locations, ICommonLookupService lookups)
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

        // The column is char(2); a longer value reached PostgreSQL and came back as a 500
        // (launch audit A2-003).
        RuleFor(x => x.CountryCode)
            .Length(2)
            .WithMessage("رمز الدولة يجب أن يتكون من حرفين.")
            .When(x => !string.IsNullOrWhiteSpace(x.CountryCode));

        // The text columns are varchar(150). A longer value reached PostgreSQL as a 500 (QA Phase 12, P12-D01).
        RuleFor(x => x.Name).MaximumLength(150).NoNulCharacters().When(x => x.Name is not null);
        RuleFor(x => x.City).MaximumLength(150).NoNulCharacters().When(x => x.City is not null);
        RuleFor(x => x.Region).MaximumLength(150).NoNulCharacters().When(x => x.Region is not null);

        // The location and type ids are foreign keys: an unknown or negative id used to come back as a 500.
        // The checker also rejects a district or neighborhood that belongs to another governorate.
        RuleFor(x => x)
            .MustAsync((x, ct) => locations.IsConsistentAsync(x.GovernorateId, x.DistrictId, x.NeighborhoodId, ct))
            .When(x => x.GovernorateId.HasValue || x.DistrictId.HasValue || x.NeighborhoodId.HasValue)
            .WithName("Location")
            .WithMessage("الموقع المحدد غير صحيح.");

        RuleFor(x => x.PropertyTypeId)
            .MustAsync(async (id, ct) => (await lookups.GetPropertyTypeCatalogAsync(ct)).Any(t => t.Id == id))
            .When(x => x.PropertyTypeId.HasValue)
            .WithMessage("نوع العقار غير معروف.");

        RuleFor(x => x.ListingType).IsInEnum().When(x => x.ListingType.HasValue);

        // A range that can never match anything (negative, or minimum above maximum) only makes the
        // background matcher scan for nothing (QA Phase 12, P12-D02).
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).When(x => x.MinPrice.HasValue).WithinMoneyRange();
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0).When(x => x.MaxPrice.HasValue).WithinMoneyRange();
        RuleFor(x => x.MinArea).GreaterThanOrEqualTo(0).When(x => x.MinArea.HasValue).WithinAreaRange();
        RuleFor(x => x.MaxArea).GreaterThanOrEqualTo(0).When(x => x.MaxArea.HasValue).WithinAreaRange();
        RuleFor(x => x.MinRooms).InclusiveBetween(0, MaxRooms).When(x => x.MinRooms.HasValue);
        RuleFor(x => x.MaxRooms).InclusiveBetween(0, MaxRooms).When(x => x.MaxRooms.HasValue);

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice!.Value)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue)
            .WithMessage("الحد الأقصى للسعر يجب ألا يقل عن الحد الأدنى.");
        RuleFor(x => x.MaxArea)
            .GreaterThanOrEqualTo(x => x.MinArea!.Value)
            .When(x => x.MinArea.HasValue && x.MaxArea.HasValue)
            .WithMessage("الحد الأقصى للمساحة يجب ألا يقل عن الحد الأدنى.");
        RuleFor(x => x.MaxRooms)
            .GreaterThanOrEqualTo(x => x.MinRooms!.Value)
            .When(x => x.MinRooms.HasValue && x.MaxRooms.HasValue)
            .WithMessage("الحد الأقصى للغرف يجب ألا يقل عن الحد الأدنى.");
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
