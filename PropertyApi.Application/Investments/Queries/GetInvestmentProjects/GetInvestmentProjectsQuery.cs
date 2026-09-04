using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjects;

public sealed record GetInvestmentProjectsQuery(InvestmentProjectFilterDto Filter)
    : IRequest<PagedResult<InvestmentProjectListDto>>;
