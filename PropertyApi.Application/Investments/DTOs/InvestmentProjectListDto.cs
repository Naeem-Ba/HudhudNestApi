using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.DTOs;

/// <summary>
/// Lightweight row for the public project list — deliberately excludes financials/risk/documents
/// detail so the list endpoint stays cheap (Phase 1 spec §32: no per-row Include chains).
/// </summary>
public sealed record InvestmentProjectListDto(
    Guid Id,
    string Title,
    string? ShortDescription,
    InvestmentProjectType ProjectType,
    InvestmentProjectStatus Status,
    string PropertyCity,
    string? PropertyCountryCode,
    string? PropertyMainImageUrl,
    decimal TargetAmount,
    decimal RaisedAmount,
    decimal MinimumInvestment,
    decimal? MaximumInvestment,
    string Currency,
    int InvestmentTermMonths,
    decimal ExpectedReturnMin,
    decimal ExpectedReturnMax,
    InvestmentRiskLevel? RiskLevel,
    DateTime? PublishedAt)
{
    public decimal RemainingAmount => Math.Max(0, TargetAmount - RaisedAmount);

    public decimal FundingProgressPercent =>
        TargetAmount <= 0 ? 0 : Math.Min(100, Math.Round(RaisedAmount / TargetAmount * 100, 2));
}
