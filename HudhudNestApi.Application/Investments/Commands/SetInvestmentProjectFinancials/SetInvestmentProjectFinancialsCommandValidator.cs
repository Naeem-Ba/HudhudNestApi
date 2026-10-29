using FluentValidation;
using HudhudNestApi.Application.Common.Validation;

namespace HudhudNestApi.Application.Investments.Commands.SetInvestmentProjectFinancials;

public sealed class SetInvestmentProjectFinancialsCommandValidator
    : AbstractValidator<SetInvestmentProjectFinancialsCommand>
{
    public SetInvestmentProjectFinancialsCommandValidator()
    {
        RuleFor(x => x.InvestmentProjectId).NotEmpty();

        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.RenovationCost).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.ConstructionCost).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.Taxes).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.NotaryCost).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.BrokerCost).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.FinancingCost).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.OperatingCost).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.ContingencyReserve).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.ExpectedRevenue).GreaterThanOrEqualTo(0).WithinMoneyRange();
        RuleFor(x => x.ExpectedProfit).GreaterThanOrEqualTo(0).WithinMoneyRange();
    }
}
