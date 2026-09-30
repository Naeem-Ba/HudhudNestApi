using FluentValidation;

namespace HudhudNestApi.Application.Services.Commands.UpdateServiceOffering;

public sealed class UpdateServiceOfferingCommandValidator : AbstractValidator<UpdateServiceOfferingCommand>
{
    public UpdateServiceOfferingCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.ServiceOfferingId).NotEmpty();

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("عنوان الخدمة مطلوب.")
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .MaximumLength(3000)
            .When(x => x.Description is not null);

        RuleFor(x => x.BasePrice)
            .GreaterThanOrEqualTo(0).WithMessage("سعر الخدمة لا يمكن أن يكون سالباً.")
            .When(x => x.BasePrice is not null);

        RuleFor(x => x.CurrencyId)
            .NotNull().WithMessage("عملة السعر مطلوبة عند تحديد سعر للخدمة.")
            .When(x => x.BasePrice is not null);

        RuleFor(x => x.EstimatedDurationDays)
            .GreaterThanOrEqualTo(0)
            .When(x => x.EstimatedDurationDays is not null);
    }
}
