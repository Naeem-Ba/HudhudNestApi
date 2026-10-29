using FluentValidation;
using HudhudNestApi.Application.Common.Validation;
using HudhudNestApi.Application.Investments.Validation;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Application.Investments.Commands.CreateInvestmentProject;

public sealed class CreateInvestmentProjectCommandValidator : AbstractValidator<CreateInvestmentProjectCommand>
{
    public CreateInvestmentProjectCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("عنوان المشروع مطلوب.")
            .MaximumLength(200)
            .NoNulCharacters()
            .MustNotImplyGuaranteedReturn();

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("وصف المشروع مطلوب.")
            .MaximumLength(8000)
            .NoNulCharacters()
            .MustNotImplyGuaranteedReturn();

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3).WithMessage("رمز العملة يجب أن يكون مكوّنًا من 3 أحرف (ISO 4217).")
            .Must(SupportedPreferences.IsSupportedCurrency).WithMessage("عملة المشروع غير مدعومة.");

        RuleFor(x => x.ProjectType).IsInEnum();
    }
}
