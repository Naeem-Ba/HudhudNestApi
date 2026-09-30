using FluentValidation;
using HudhudNestApi.Application.Investments.Validation;

namespace HudhudNestApi.Application.Investments.Commands.CreateInvestmentProject;

public sealed class CreateInvestmentProjectCommandValidator : AbstractValidator<CreateInvestmentProjectCommand>
{
    public CreateInvestmentProjectCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("عنوان المشروع مطلوب.")
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("وصف المشروع مطلوب.")
            .MaximumLength(8000)
            .MustNotImplyGuaranteedReturn();

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3).WithMessage("رمز العملة يجب أن يكون مكوّنًا من 3 أحرف (ISO 4217).");

        RuleFor(x => x.ProjectType).IsInEnum();
    }
}
