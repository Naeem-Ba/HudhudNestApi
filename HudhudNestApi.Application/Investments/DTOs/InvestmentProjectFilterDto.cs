using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.DTOs;

/// <summary>Server-side filter/sort/paging for the public project list (Phase 1 spec §22).</summary>
public sealed record InvestmentProjectFilterDto
{
    public string? Search { get; init; }
    public InvestmentProjectType? ProjectType { get; init; }
    public InvestmentRiskLevel? RiskLevel { get; init; }
    public int? MinTermMonths { get; init; }
    public int? MaxTermMonths { get; init; }
    public decimal? MinTargetAmount { get; init; }
    public decimal? MaxTargetAmount { get; init; }
    public decimal? MinExpectedReturn { get; init; }
    public string? City { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>Same filter set, plus Status — admin-only, since Status filtering across
/// non-published projects must never be reachable by the public list query.</summary>
public sealed record AdminInvestmentProjectFilterDto
{
    public string? Search { get; init; }
    public InvestmentProjectStatus? Status { get; init; }
    public InvestmentProjectType? ProjectType { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
