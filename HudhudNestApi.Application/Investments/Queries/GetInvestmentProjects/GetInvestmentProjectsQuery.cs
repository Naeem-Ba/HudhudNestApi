using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjects;

public sealed record GetInvestmentProjectsQuery(InvestmentProjectFilterDto Filter)
    : IRequest<PagedResult<InvestmentProjectListDto>>;
