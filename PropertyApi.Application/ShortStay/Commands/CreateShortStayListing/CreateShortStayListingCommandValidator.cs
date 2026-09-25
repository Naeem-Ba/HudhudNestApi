using FluentValidation;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;

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
        RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180m, 180m).When(x => x.Longitude.HasValue);
        RuleFor(x => x.Longitude).NotNull().When(x => x.Latitude.HasValue)
            .WithMessage("حدّد خط العرض وخط الطول معًا.");
        RuleFor(x => x.Latitude).NotNull().When(x => x.Longitude.HasValue)
            .WithMessage("حدّد خط العرض وخط الطول معًا.");
        // 0,0 is what a client sends when it has no location; it is a point in the Atlantic.
        RuleFor(x => x.Latitude).Must((x, _) => !(x.Latitude == 0m && x.Longitude == 0m))
            .WithMessage("حدّد موقع الإعلان على الخريطة.");
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.LocationVisibility)
            .Must(v => Enum.TryParse<LocationVisibility>(v, ignoreCase: true, out _))
            .WithMessage("قيمة LocationVisibility غير معروفة.")
            .When(x => x.LocationVisibility is not null);
    }
}
