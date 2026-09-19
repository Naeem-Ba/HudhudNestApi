using FluentValidation;
using PropertyApi.Domain.ShortStay.Enums;

namespace PropertyApi.Application.ShortStay.Commands.CreateBooking;

public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.GuestId).NotEmpty();

        RuleFor(x => x.CheckIn)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("لا يمكن الحجز بتاريخ ماضٍ.");

        RuleFor(x => x.CheckOut)
            .GreaterThan(x => x.CheckIn)
            .WithMessage("تاريخ المغادرة يجب أن يكون بعد تاريخ الوصول.");

        // Security audit finding (2026-09-18): no upper bound existed on stay length -- same
        // missing-cap class as GetUnitAvailabilityQueryValidator, applied to the actual booking
        // write path this time (not just the read-only availability check).
        RuleFor(x => x)
            .Must(x => x.CheckOut.DayNumber - x.CheckIn.DayNumber <= 366)
            .WithMessage("مدة الحجز لا يمكن أن تتجاوز 366 يوماً.");

        RuleFor(x => x.Adults).GreaterThanOrEqualTo(1)
            .WithMessage("يجب أن يكون هناك بالغ واحد على الأقل.");
        RuleFor(x => x.Children).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Infants).GreaterThanOrEqualTo(0);

        RuleFor(x => x.PaymentMethod)
            .Must(v => Enum.TryParse<PaymentMethod>(v, ignoreCase: true, out _))
            .WithMessage("طريقة دفع غير معروفة.");

        RuleFor(x => x.HouseRulesAccepted)
            .Equal(true)
            .WithMessage("يجب الموافقة على قواعد المنزل قبل إتمام الحجز.");
    }
}
