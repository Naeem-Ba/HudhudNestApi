using FluentValidation;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Commands.CreateShortStayListing;

public sealed class CreateShortStayListingCommandValidator : AbstractValidator<CreateShortStayListingCommand>
{
    public CreateShortStayListingCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.AccommodationTypeId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Capacity).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Bedrooms).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Bathrooms).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DefaultBasePricePerNight).GreaterThan(0);
        // Optional: clients built before the currency field existed omit it and get the
        // system default (SYP) instead of a 400. When present it must be a supported code.
        RuleFor(x => x.CurrencyCode)
            .Must(c => !string.IsNullOrWhiteSpace(c) && ShortStayListing.SupportedCurrencyCodes.Contains(c.Trim()))
            .When(x => x.CurrencyCode is not null)
            .WithMessage("رمز العملة غير مدعوم.");
        RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m);
        RuleFor(x => x.Longitude).InclusiveBetween(-180m, 180m);
    }
}
