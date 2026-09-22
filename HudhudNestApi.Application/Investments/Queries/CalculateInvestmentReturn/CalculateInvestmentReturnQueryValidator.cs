using FluentValidation;

namespace HudhudNestApi.Application.Investments.Queries.CalculateInvestmentReturn;

public sealed class CalculateInvestmentReturnQueryValidator : AbstractValidator<CalculateInvestmentReturnQuery>
{
    public CalculateInvestmentReturnQueryValidator()
    {
        RuleFor(x => x.InvestmentProjectId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.TermMonths).GreaterThan(0).When(x => x.TermMonths.HasValue);
    }
}
