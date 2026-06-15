using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using PropertyApi.Application.Listings.Commands.CreateProperty;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Listings.Validators;

public sealed class CreatePropertyCommandValidator : AbstractValidator<CreatePropertyCommand>
{
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

    public CreatePropertyCommandValidator()
    {
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

        RuleFor(x => x.Rooms)
            .InclusiveBetween(1, 50)
            .When(x => x.Rooms.HasValue);

        RuleFor(x => x.Area)
            .GreaterThan(10)
            .When(x => x.Area.HasValue)
            .WithMessage("Area must be greater than 10 m\u00B2.");
    }
}

