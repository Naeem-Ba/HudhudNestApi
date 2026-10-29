using FluentValidation;
using HudhudNestApi.Application.Common.Validation;
using HudhudNestApi.Application.Investments.Validation;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Application.Investments.Commands.UpdateInvestmentProject;

public sealed class UpdateInvestmentProjectCommandValidator : AbstractValidator<UpdateInvestmentProjectCommand>
{
    public UpdateInvestmentProjectCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        // QA Phase 10, P10-D09: the title is shown on the list and in shares, so it follows the same
        // "no promised return" rule as the description.
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).NoNulCharacters().MustNotImplyGuaranteedReturn();
        RuleFor(x => x.ShortDescription).MaximumLength(300).NoNulCharacters().MustNotImplyGuaranteedReturn();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000).NoNulCharacters().MustNotImplyGuaranteedReturn();
        RuleFor(x => x.ProjectType).IsInEnum();
        RuleFor(x => x.TargetAmount).GreaterThan(0).WithinMoneyRange();
        RuleFor(x => x.MinimumInvestment).GreaterThan(0).WithinMoneyRange()
            .LessThanOrEqualTo(x => x.TargetAmount)
            .WithMessage("الحد الأدنى للاستثمار لا يمكن أن يتجاوز المبلغ المستهدف.");
        RuleFor(x => x.MaximumInvestment)
            .GreaterThanOrEqualTo(x => x.MinimumInvestment)
            .When(x => x.MaximumInvestment.HasValue)
            .WithMessage("الحد الأقصى للاستثمار يجب أن يكون أكبر من أو يساوي الحد الأدنى.");
        RuleFor(x => x.MaximumInvestment!.Value).WithinMoneyRange().When(x => x.MaximumInvestment.HasValue);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Must(SupportedPreferences.IsSupportedCurrency)
            .WithMessage("عملة المشروع غير مدعومة.");
        RuleFor(x => x.InvestmentTermMonths).GreaterThan(0).LessThanOrEqualTo(InvestmentProject.MaxTermMonths);
        RuleFor(x => x.ExpectedReturnMin).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExpectedReturnMax)
            .GreaterThanOrEqualTo(x => x.ExpectedReturnMin)
            .WithMessage("العائد المتوقع (الأعلى) يجب أن يكون أكبر من أو يساوي العائد الأدنى.")
            .LessThanOrEqualTo(InvestmentProject.MaxExpectedReturnPercent)
            .WithMessage("العائد المتوقع السنوي لا يمكن أن يتجاوز 100%.");
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
