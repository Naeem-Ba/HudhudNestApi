using FluentValidation;
using HudhudNestApi.Application.Investments.Validation;

namespace HudhudNestApi.Application.Investments.Commands.SetInvestmentProjectRiskAssessment;

public sealed class SetInvestmentProjectRiskAssessmentCommandValidator
    : AbstractValidator<SetInvestmentProjectRiskAssessmentCommand>
{
    public SetInvestmentProjectRiskAssessmentCommandValidator()
    {
        RuleFor(x => x.InvestmentProjectId).NotEmpty();

        RuleFor(x => x.RiskLevel).IsInEnum();
        RuleFor(x => x.MarketRisk).IsInEnum();
        RuleFor(x => x.LiquidityRisk).IsInEnum();
        RuleFor(x => x.ProjectRisk).IsInEnum();
        RuleFor(x => x.FinancingRisk).IsInEnum();
        RuleFor(x => x.DeveloperRisk).IsInEnum();

        RuleFor(x => x.RiskScore).InclusiveBetween(0, 100);

        RuleFor(x => x.RiskSummary)
            .NotEmpty().WithMessage("ملخص المخاطر مطلوب.")
            .MaximumLength(4000)
            .MustNotImplyGuaranteedReturn();
    }
}
