using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectFinancials;

/// <summary>Only exposed for a Published project — see the Published-status gate in
/// InvestmentProjectFinancialsRepository.GetSummaryAsync.</summary>
public sealed class GetInvestmentProjectFinancialsQueryHandler
    : IRequestHandler<GetInvestmentProjectFinancialsQuery, InvestmentFinancialSummaryDto?>
{
    private readonly IInvestmentProjectFinancialsRepository _financials;

    public GetInvestmentProjectFinancialsQueryHandler(IInvestmentProjectFinancialsRepository financials) =>
        _financials = financials;

    public Task<InvestmentFinancialSummaryDto?> Handle(GetInvestmentProjectFinancialsQuery request, CancellationToken ct) =>
        _financials.GetSummaryAsync(request.InvestmentProjectId, ct);
}
