using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.DTOs;

/// <summary>
/// Full public project detail page payload. Never includes internal-only fields (reviewer
/// notes, rejection reason, storage keys) — see AdminInvestmentProjectDto for the staff view.
/// </summary>
public sealed record InvestmentProjectDetailsDto(
    Guid Id,
    Guid PropertyId,
    string Title,
    string? ShortDescription,
    string Description,
    InvestmentProjectType ProjectType,
    InvestmentProjectStatus Status,
    string PropertyTitle,
    string PropertyCity,
    string? PropertyCountryCode,
    decimal? PropertyLatitude,
    decimal? PropertyLongitude,
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
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateTime? PublishedAt)
{
    public decimal RemainingAmount => Math.Max(0, TargetAmount - RaisedAmount);

    public decimal FundingProgressPercent =>
        TargetAmount <= 0 ? 0 : Math.Min(100, Math.Round(RaisedAmount / TargetAmount * 100, 2));
}
