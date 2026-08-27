using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Commands.CreateProperty;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Application.Listings.Validators;

public sealed class CreatePropertyCommandValidator : AbstractValidator<CreatePropertyCommand>
{
    private readonly ICommonLookupService _lookups;

    // ISO 3166-1 alpha-2 codes (extend as needed)
    private static readonly HashSet<string> ValidCountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DE", "SY", "US", "GB", "FR", "AE", "SA", "TR", "EG", "JO", "LB"
        // Add more as you expand
    };

    // ISO 4217 currency codes
    private static readonly HashSet<string> ValidCurrencyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "EUR", "USD", "GBP", "SYP", "TRY", "AED", "SAR", "EGP", "JOD", "LBP"
    };

    public CreatePropertyCommandValidator(ICommonLookupService lookups)
    {
        _lookups = lookups;

        RuleFor(x => x.OwnerId)
            .NotEmpty().WithMessage("OwnerId is required.");

        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .NotEmpty()
            .MaximumLength(5000);

        RuleFor(x => x.City)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.Street)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.Region)
            .MaximumLength(150)
            .When(x => x.Region is not null);

        RuleFor(x => x.PostalCode)
            .MaximumLength(20)
            .When(x => x.PostalCode is not null);

        // Structured location — mandatory as of this pass (see
        // docs/phase-0-implementation.md section 9): the free-text City/Region
        // inputs were dropped from the Angular form in favor of these
        // governorate/district selects, so they must now always be present.
        // NeighborhoodId stays optional (seed coverage is Damascus/Homs/
        // Latakia only) — NeighborhoodText is the manual fallback everywhere
        // else, also optional (a listing may simply not specify one).
        RuleFor(x => x.GovernorateId)
            .NotNull()
            .WithMessage("Governorate is required.");

        // District: pick from the seeded list (DistrictId) OR type a name not
        // in the list yet (DistrictText) — see LocationSuggestion. At least
        // one must be present; both is fine too (DistrictId wins downstream).
        RuleFor(x => x)
            .Must(x => x.DistrictId.HasValue || !string.IsNullOrWhiteSpace(x.DistrictText))
            .WithMessage("District is required — select one from the list, or type its name if it isn't listed.");

        RuleFor(x => x.DistrictText)
            .MaximumLength(150)
            .When(x => x.DistrictText is not null);

        RuleFor(x => x.NeighborhoodText)
            .MaximumLength(150)
            .When(x => x.NeighborhoodText is not null);

        RuleFor(x => x.PropertyTypeId)
            .NotNull()
            .WithMessage("Property type is required.");

        RuleFor(x => x.CountryCode)
            .NotEmpty()
            .Length(2)
            .Must(c => ValidCountryCodes.Contains(c))
            .WithMessage("Invalid ISO 3166-1 country code.");

        RuleFor(x => x.CurrencyCode)
            .NotEmpty()
            .Length(3)
            .Must(c => ValidCurrencyCodes.Contains(c))
            .WithMessage("Invalid ISO 4217 currency code.");


        // ForRent: ColdRent is required.
        RuleFor(x => x.ColdRent)
            .NotNull()
            .WithMessage("Cold rent is required for rental listings.")
            .When(x => x.ListingType == ListingType.ForRent);

        RuleFor(x => x.ColdRent)
            .GreaterThan(0)
            .WithMessage("Cold rent must be greater than zero.")
            .When(x => x.ListingType == ListingType.ForRent && x.ColdRent.HasValue);

        RuleFor(x => x.WarmRent)
            .GreaterThan(0)
            .WithMessage("Warm rent must be greater than zero.")
            .When(x => x.ListingType == ListingType.ForRent && x.WarmRent.HasValue);

        RuleFor(x => x)
            .Must(x => !x.WarmRent.HasValue || !x.ColdRent.HasValue || x.WarmRent >= x.ColdRent)
            .WithMessage("WarmRent must be greater than or equal to ColdRent.");

        RuleFor(x => x.PurchasePrice)
            .NotNull()
            .WithMessage("Purchase price is required for sale listings.")
            .GreaterThan(0)
            .WithMessage("Purchase price must be greater than zero.")
            .When(x => x.ListingType == ListingType.ForSale);


        // ForRentAndSale: both ColdRent and PurchasePrice are required.
        RuleFor(x => x)
            .Must(x => x.ColdRent > 0 && x.PurchasePrice > 0)
            .WithMessage("Combined listings require both ColdRent and PurchasePrice.")
            .When(x => x.ListingType == ListingType.ForRentAndSale);

        RuleFor(x => x.AmenityIds)
            .Must(ids => ids is null || ids.Count == ids.Distinct().Count())
            .WithMessage("AmenityIds must not contain duplicates.");

        // Coordinates: both or neither
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90)
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180)
            .When(x => x.Longitude.HasValue);

        RuleFor(x => x)
            .Must(x => x.Latitude.HasValue == x.Longitude.HasValue)
            .WithMessage("Both Latitude and Longitude must be provided together, or neither.");

        RuleFor(x => x.Deposit)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Deposit.HasValue);

        RuleFor(x => x.AdditionalCosts)
            .GreaterThanOrEqualTo(0)
            .When(x => x.AdditionalCosts.HasValue);

        RuleFor(x => x.Rooms)
            .InclusiveBetween(1, 50)
            .When(x => x.Rooms.HasValue);

        // Area >10 only makes sense in square meters \u2014 a 2-Dunum plot (\u22482000 m\u00B2)
        // is a perfectly normal, small-looking number in its own unit.
        RuleFor(x => x.Area)
            .GreaterThan(10)
            .When(x => x.Area.HasValue && x.AreaUnit == AreaUnit.SquareMeter)
            .WithMessage("Area must be greater than 10 m\u00B2.");

        RuleFor(x => x.Area)
            .GreaterThan(0)
            .When(x => x.Area.HasValue && x.AreaUnit != AreaUnit.SquareMeter)
            .WithMessage("Area must be greater than zero.");

        RuleFor(x => x.AreaUnit)
            .IsInEnum();

        RuleFor(x => x.Floor)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Floor.HasValue);

        // -- Ownership type (LegalStatus) ---------------------------
        // Required for Sale/ForRentAndSale \u2014 a sale ad with no ownership
        // document type is not usable in the Syrian market. Not required for a
        // pure rental (the entity still defaults to Unknown, which is fine there).
        RuleFor(x => x.LegalStatus)
            .IsInEnum()
            .When(x => x.LegalStatus.HasValue);

        RuleFor(x => x.LegalStatus)
            .Must(v => v.HasValue && v.Value != LegalStatusType.Unknown)
            .WithMessage("Ownership type is required for sale listings.")
            .When(x => x.ListingType is ListingType.ForSale or ListingType.ForRentAndSale);

        RuleFor(x => x.FurnishingStatus)
            .IsInEnum()
            .When(x => x.FurnishingStatus.HasValue);

        // -- Rental term ----------------------------------------------
        // The current design supports only a fixed-term rental: a rental listing
        // must carry a start date, an end date after it, and a duration choice.
        RuleFor(x => x.RentalDurationType)
            .IsInEnum()
            .When(x => x.RentalDurationType.HasValue);

        RuleFor(x => x.RentalStartDate)
            .NotNull()
            .WithMessage("Rental start date is required for rental listings.")
            .When(x => x.ListingType is ListingType.ForRent or ListingType.ForRentAndSale);

        RuleFor(x => x.RentalEndDate)
            .NotNull()
            .WithMessage("Rental end date is required for rental listings.")
            .When(x => x.ListingType is ListingType.ForRent or ListingType.ForRentAndSale);

        RuleFor(x => x.RentalDurationType)
            .NotNull()
            .WithMessage("Rental duration is required for rental listings.")
            .When(x => x.ListingType is ListingType.ForRent or ListingType.ForRentAndSale);

        RuleFor(x => x)
            .Must(x => !x.RentalStartDate.HasValue || !x.RentalEndDate.HasValue
                       || x.RentalEndDate.Value > x.RentalStartDate.Value)
            .WithMessage("Rental end date must be after the start date.");

        // -- Property-type-category conditional rules ------------------
        // PropertyType is a DB-backed lookup (admin-editable), not a compiled
        // enum \u2014 Category ("Residential"/"Land"/"Commercial") has to be resolved
        // async via the same cached lookup service LookupsController uses.
        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                if (command.PropertyTypeId is not { } propertyTypeId)
                    return true; // covered by the PropertyTypeId NotNull rule above

                var category = await ResolveCategoryAsync(propertyTypeId, ct);

                // A land ad with no size is not usable \u2014 every other field
                // (rooms/floor/etc.) is already optional and needs no new rule.
                if (category == "Land")
                    return command.Area.HasValue;

                return true;
            })
            .WithMessage("Area is required for land listings.")
            .WithName("Area");
    }

    private async Task<string?> ResolveCategoryAsync(int propertyTypeId, CancellationToken ct)
    {
        var catalog = await _lookups.GetPropertyTypeCatalogAsync(ct);
        return catalog.FirstOrDefault(pt => pt.Id == propertyTypeId)?.Category;
    }
}

