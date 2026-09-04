using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetAdminInvestmentProjects;

public sealed record GetAdminInvestmentProjectsQuery(AdminInvestmentProjectFilterDto Filter)
    : IRequest<PagedResult<AdminInvestmentProjectListDto>>;
