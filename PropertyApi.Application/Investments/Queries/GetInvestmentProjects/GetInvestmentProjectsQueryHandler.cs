using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjects;

public sealed class GetInvestmentProjectsQueryHandler
    : IRequestHandler<GetInvestmentProjectsQuery, PagedResult<InvestmentProjectListDto>>
{
    private readonly IInvestmentProjectRepository _projects;

    public GetInvestmentProjectsQueryHandler(IInvestmentProjectRepository projects) => _projects = projects;

    public Task<PagedResult<InvestmentProjectListDto>> Handle(GetInvestmentProjectsQuery request, CancellationToken ct) =>
        _projects.GetPublishedListAsync(request.Filter, ct);
}
