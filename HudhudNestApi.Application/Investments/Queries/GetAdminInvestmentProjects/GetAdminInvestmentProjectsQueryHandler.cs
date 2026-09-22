using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetAdminInvestmentProjects;

public sealed class GetAdminInvestmentProjectsQueryHandler
    : IRequestHandler<GetAdminInvestmentProjectsQuery, PagedResult<AdminInvestmentProjectListDto>>
{
    private readonly IInvestmentProjectRepository _projects;

    public GetAdminInvestmentProjectsQueryHandler(IInvestmentProjectRepository projects) => _projects = projects;

    public Task<PagedResult<AdminInvestmentProjectListDto>> Handle(GetAdminInvestmentProjectsQuery request, CancellationToken ct) =>
        _projects.GetAdminListAsync(request.Filter, ct);
}
