using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.SetInvestmentProjectFinancials;

/// <summary>Upsert — creates the financials record on first call, updates it thereafter.</summary>
public sealed record SetInvestmentProjectFinancialsCommand(
    Guid InvestmentProjectId,
    decimal PurchasePrice,
    decimal RenovationCost,
    decimal ConstructionCost,
    decimal Taxes,
    decimal NotaryCost,
    decimal BrokerCost,
    decimal FinancingCost,
    decimal OperatingCost,
    decimal ContingencyReserve,
    decimal ExpectedRevenue,
    decimal ExpectedProfit) : IRequest;
