using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectRisk;

public sealed record GetInvestmentProjectRiskQuery(Guid InvestmentProjectId) : IRequest<InvestmentRiskDto?>;
