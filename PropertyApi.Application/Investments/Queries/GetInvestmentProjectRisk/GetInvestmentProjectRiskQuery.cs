using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjectRisk;

public sealed record GetInvestmentProjectRiskQuery(Guid InvestmentProjectId) : IRequest<InvestmentRiskDto?>;
