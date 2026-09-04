using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjectFinancials;

public sealed record GetInvestmentProjectFinancialsQuery(Guid InvestmentProjectId)
    : IRequest<InvestmentFinancialSummaryDto?>;
