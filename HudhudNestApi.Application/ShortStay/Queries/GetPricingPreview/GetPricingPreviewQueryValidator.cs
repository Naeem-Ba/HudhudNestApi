using FluentValidation;

namespace HudhudNestApi.Application.ShortStay.Queries.GetPricingPreview;

public sealed class GetPricingPreviewQueryValidator : AbstractValidator<GetPricingPreviewQuery>
{
    public GetPricingPreviewQueryValidator()
    {
        RuleFor(x => x.UnitId).NotEmpty();

        RuleFor(x => x.CheckIn)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("لا يمكن حساب السعر لتاريخ ماضٍ.");

        RuleFor(x => x.CheckOut)
            .GreaterThan(x => x.CheckIn)
            .WithMessage("تاريخ المغادرة يجب أن يكون بعد تاريخ الوصول.");

        RuleFor(x => x.Adults).GreaterThanOrEqualTo(1)
            .WithMessage("يجب أن يكون هناك بالغ واحد على الأقل.");
        RuleFor(x => x.Children).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Infants).GreaterThanOrEqualTo(0);
    }
}
