using MediatR;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Commands.SetInvestmentProjectRiskAssessment;

/// <summary>Upsert — creates the risk assessment on first call, updates it thereafter.</summary>
public sealed record SetInvestmentProjectRiskAssessmentCommand(
    Guid InvestmentProjectId,
    InvestmentRiskLevel RiskLevel,
    InvestmentRiskLevel MarketRisk,
    InvestmentRiskLevel LiquidityRisk,
    InvestmentRiskLevel ProjectRisk,
    InvestmentRiskLevel FinancingRisk,
    InvestmentRiskLevel DeveloperRisk,
    int RiskScore,
    string RiskSummary) : IRequest;
