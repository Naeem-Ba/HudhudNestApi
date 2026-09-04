namespace PropertyApi.Application.Investments.DTOs;

public sealed record InvestmentCalculatorRequestDto(
    decimal Amount,
    int? TermMonths);

public sealed record InvestmentCalculatorScenarioDto(
    decimal InitialAmount,
    decimal EstimatedReturnAmount,
    decimal EstimatedTotal,
    decimal AnnualizedReturnPercent);

/// <summary>
/// Result of the Investment Calculator — always indicative, never persisted, never tied to a
/// payment (Phase 1 spec §21). Every response carries an explicit, non-optional disclaimer.
/// </summary>
public sealed record InvestmentCalculatorResultDto(
    Guid InvestmentProjectId,
    decimal Amount,
    int TermMonths,
    string Currency,
    InvestmentCalculatorScenarioDto Conservative,
    InvestmentCalculatorScenarioDto Base,
    InvestmentCalculatorScenarioDto Optimistic,
    string Disclaimer);
