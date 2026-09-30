using FluentValidation;
using HudhudNestApi.Domain.ShortStay.Enums;

namespace HudhudNestApi.Application.ShortStay.Commands.UpdateShortStayListing;

/// <summary>
/// Security audit finding (2026-09-18): this command had no validator at all, so
/// <c>Enum.Parse</c> on <c>LocationVisibility</c>/<c>PoolType</c>/<c>PoolLocation</c> in the
/// handler threw an unhandled <see cref="ArgumentException"/> on any unrecognized string --
/// ExceptionHandlingMiddleware has no specific catch for that, so it fell through to the
/// generic handler and returned an opaque 500 instead of a clean 400. Mirrors
/// CreateBookingCommandValidator's <c>Enum.TryParse</c> guard pattern for the same reason.
/// </summary>
public sealed class UpdateShortStayListingCommandValidator : AbstractValidator<UpdateShortStayListingCommand>
{
    public UpdateShortStayListingCommandValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Capacity).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Bedrooms).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Bathrooms).GreaterThanOrEqualTo(0);
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
            .WithMessage("قيمة LocationVisibility غير معروفة.");

        RuleFor(x => x.PoolType)
            .Must(v => Enum.TryParse<PoolType>(v, ignoreCase: true, out _))
            .WithMessage("قيمة PoolType غير معروفة.")
            .When(x => x.PoolType is not null);

        RuleFor(x => x.PoolLocation)
            .Must(v => Enum.TryParse<PoolLocation>(v, ignoreCase: true, out _))
            .WithMessage("قيمة PoolLocation غير معروفة.")
            .When(x => x.PoolLocation is not null);

        RuleFor(x => x.CleaningFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExtraGuestFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExtraBedFee).GreaterThanOrEqualTo(0);

        RuleFor(x => x.CancellationFreeCancellationDays).GreaterThanOrEqualTo(0);

        RuleFor(x => x.DepositPercentage)
            .InclusiveBetween(0m, 100m)
            .When(x => x.DepositPercentage.HasValue);
    }
}
