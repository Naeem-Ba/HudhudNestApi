using FluentValidation;

namespace PropertyApi.Application.Investments.Commands.SetInvestmentProjectFinancials;

public sealed class SetInvestmentProjectFinancialsCommandValidator
    : AbstractValidator<SetInvestmentProjectFinancialsCommand>
{
    public SetInvestmentProjectFinancialsCommandValidator()
    {
        RuleFor(x => x.InvestmentProjectId).NotEmpty();

        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RenovationCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ConstructionCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Taxes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.NotaryCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BrokerCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.FinancingCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OperatingCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ContingencyReserve).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExpectedRevenue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExpectedProfit).GreaterThanOrEqualTo(0);
    }
}
