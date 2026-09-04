using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.CalculateInvestmentReturn;

/// <summary>Educational/indicative only — see InvestmentCalculator. Never persists, never
/// creates an investment (Phase 1 spec §21).</summary>
public sealed record CalculateInvestmentReturnQuery(
    Guid InvestmentProjectId,
    decimal Amount,
    int? TermMonths) : IRequest<InvestmentCalculatorResultDto>;
