using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetAdminInvestmentProjects;

public sealed class GetAdminInvestmentProjectsQueryHandler
    : IRequestHandler<GetAdminInvestmentProjectsQuery, PagedResult<AdminInvestmentProjectListDto>>
{
    private readonly IInvestmentProjectRepository _projects;

    public GetAdminInvestmentProjectsQueryHandler(IInvestmentProjectRepository projects) => _projects = projects;

    public Task<PagedResult<AdminInvestmentProjectListDto>> Handle(GetAdminInvestmentProjectsQuery request, CancellationToken ct) =>
        _projects.GetAdminListAsync(request.Filter, ct);
}
