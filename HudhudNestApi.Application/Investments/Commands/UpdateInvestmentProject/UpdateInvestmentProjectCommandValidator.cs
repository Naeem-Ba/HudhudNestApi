using FluentValidation;
using HudhudNestApi.Application.Investments.Validation;

namespace HudhudNestApi.Application.Investments.Commands.UpdateInvestmentProject;

public sealed class UpdateInvestmentProjectCommandValidator : AbstractValidator<UpdateInvestmentProjectCommand>
{
    public UpdateInvestmentProjectCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ShortDescription).MaximumLength(300).MustNotImplyGuaranteedReturn();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000).MustNotImplyGuaranteedReturn();
        RuleFor(x => x.ProjectType).IsInEnum();

        RuleFor(x => x.TargetAmount).GreaterThan(0);
        RuleFor(x => x.MinimumInvestment).GreaterThan(0);
        RuleFor(x => x.MaximumInvestment)
            .GreaterThanOrEqualTo(x => x.MinimumInvestment)
            .When(x => x.MaximumInvestment.HasValue)
            .WithMessage("الحد الأقصى للاستثمار يجب أن يكون أكبر من أو يساوي الحد الأدنى.");

        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.InvestmentTermMonths).GreaterThan(0);

        RuleFor(x => x.ExpectedReturnMin).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExpectedReturnMax)
            .GreaterThanOrEqualTo(x => x.ExpectedReturnMin)
            .WithMessage("العائد المتوقع (الأعلى) يجب أن يكون أكبر من أو يساوي العائد الأدنى.");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate!.Value)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue)
            .WithMessage("تاريخ انتهاء المشروع يجب أن يكون بعد تاريخ البدء.");

        RuleFor(x => x.RaisedAmount)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.TargetAmount)
            .WithMessage("المبلغ المجمّع لا يمكن أن يتجاوز المبلغ المستهدف.");
    }
}
