using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.DTOs;

public sealed record InvestmentRiskDto(
    Guid InvestmentProjectId,
    InvestmentRiskLevel RiskLevel,
    InvestmentRiskLevel MarketRisk,
    InvestmentRiskLevel LiquidityRisk,
    InvestmentRiskLevel ProjectRisk,
    InvestmentRiskLevel FinancingRisk,
    InvestmentRiskLevel DeveloperRisk,
    int RiskScore,
    string RiskSummary);
