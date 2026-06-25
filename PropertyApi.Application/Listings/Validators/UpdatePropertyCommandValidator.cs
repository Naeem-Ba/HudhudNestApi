using FluentValidation;
using PropertyApi.Application.Listings.Commands.UpdateProperty;

namespace PropertyApi.Application.Listings.Validators;

public sealed class UpdatePropertyCommandValidator
    : AbstractValidator<UpdatePropertyCommand>
{
    private static readonly HashSet<string> ValidCountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DE", "SY", "US", "GB", "FR", "AE", "SA", "TR", "EG", "JO", "LB"
    };

    private static readonly HashSet<string> ValidCurrencyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "EUR", "USD", "GBP", "SYP", "TRY", "AED", "SAR", "EGP", "JOD", "LBP"
    };

    public UpdatePropertyCommandValidator()
    {
        RuleFor(command => command.PropertyId)
            .NotEmpty();

        RuleFor(command => command.RequestingUserId)
            .NotEmpty();

        RuleFor(command => command.Title)
            .NotEmpty()
            .MaximumLength(200)
            .When(command => command.Title is not null);

        RuleFor(command => command.Description)
            .NotEmpty()
            .MaximumLength(5000)
            .When(command => command.Description is not null);

        RuleFor(command => command.Street)
            .MaximumLength(300)
            .When(command => command.Street is not null);

        RuleFor(command => command.City)
            .NotEmpty()
            .MaximumLength(150)
            .When(command => command.City is not null);

        RuleFor(command => command.Region)
            .MaximumLength(150)
            .When(command => command.Region is not null);

        RuleFor(command => command.CountryCode)
            .Length(2)
            .Must(countryCode => countryCode is not null && ValidCountryCodes.Contains(countryCode))
            .WithMessage("Invalid ISO 3166-1 country code.")
            .When(command => command.CountryCode is not null);

        RuleFor(command => command.PostalCode)
            .MaximumLength(20)
            .When(command => command.PostalCode is not null);

        RuleFor(command => command.CurrencyCode)
            .Length(3)
            .Must(currencyCode => currencyCode is not null && ValidCurrencyCodes.Contains(currencyCode))
            .WithMessage("Invalid ISO 4217 currency code.")
            .When(command => command.CurrencyCode is not null);

        RuleFor(command => command.Latitude)
            .InclusiveBetween(-90, 90)
            .When(command => command.Latitude.HasValue);

        RuleFor(command => command.Longitude)
            .InclusiveBetween(-180, 180)
            .When(command => command.Longitude.HasValue);

        RuleFor(command => command)
            .Must(command => command.Latitude.HasValue == command.Longitude.HasValue)
            .WithMessage("Both Latitude and Longitude must be provided together, or neither.")
            .When(command => command.Latitude.HasValue || command.Longitude.HasValue);

        RuleFor(command => command.ColdRent)
            .GreaterThan(0)
            .When(command => command.ColdRent.HasValue);

        RuleFor(command => command.WarmRent)
            .GreaterThan(0)
            .When(command => command.WarmRent.HasValue);

        RuleFor(command => command.PurchasePrice)
            .GreaterThan(0)
            .When(command => command.PurchasePrice.HasValue);

        RuleFor(command => command)
            .Must(command =>
                !command.WarmRent.HasValue ||
                !command.ColdRent.HasValue ||
                command.WarmRent >= command.ColdRent)
            .WithMessage("WarmRent must be greater than or equal to ColdRent.");

        RuleFor(command => command.Deposit)
            .GreaterThanOrEqualTo(0)
            .When(command => command.Deposit.HasValue);

        RuleFor(command => command.AdditionalCosts)
            .GreaterThanOrEqualTo(0)
            .When(command => command.AdditionalCosts.HasValue);

        RuleFor(command => command.Rooms)
            .InclusiveBetween(1, 50)
            .When(command => command.Rooms.HasValue);

        RuleFor(command => command.Area)
            .GreaterThan(10)
            .When(command => command.Area.HasValue);

        RuleFor(command => command.Floor)
            .GreaterThanOrEqualTo(0)
            .When(command => command.Floor.HasValue);

        RuleFor(command => command.TotalFloors)
            .GreaterThanOrEqualTo(1)
            .When(command => command.TotalFloors.HasValue);

        RuleFor(command => command)
            .Must(command =>
                !command.Floor.HasValue ||
                !command.TotalFloors.HasValue ||
                command.Floor <= command.TotalFloors)
            .WithMessage(
                "Floor cannot be greater than TotalFloors.");
    }
}
