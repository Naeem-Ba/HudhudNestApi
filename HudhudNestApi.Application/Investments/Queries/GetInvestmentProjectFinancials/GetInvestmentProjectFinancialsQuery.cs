using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectFinancials;

public sealed record GetInvestmentProjectFinancialsQuery(Guid InvestmentProjectId)
    : IRequest<InvestmentFinancialSummaryDto?>;
