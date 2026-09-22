namespace HudhudNestApi.Application.Investments.DTOs;

/// <summary>Public-safe financial overview — every field here is meant to be shown to any
/// visitor of a Published project. No internal margin/commission breakdown beyond what the
/// spec explicitly lists as public (Phase 1 spec §16).</summary>
public sealed record InvestmentFinancialSummaryDto(
    Guid InvestmentProjectId,
    decimal? PropertyEstimatedValue,
    decimal PurchasePrice,
    decimal RenovationCost,
    decimal ConstructionCost,
    decimal Taxes,
    decimal NotaryCost,
    decimal BrokerCost,
    decimal FinancingCost,
    decimal OperatingCost,
    decimal ContingencyReserve,
    decimal TotalProjectCost,
    decimal ExpectedRevenue,
    decimal ExpectedProfit);
