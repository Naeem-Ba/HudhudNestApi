using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetAdminInvestmentProjects;

public sealed record GetAdminInvestmentProjectsQuery(AdminInvestmentProjectFilterDto Filter)
    : IRequest<PagedResult<AdminInvestmentProjectListDto>>;
