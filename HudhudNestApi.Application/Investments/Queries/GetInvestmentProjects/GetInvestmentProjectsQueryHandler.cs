using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjects;

public sealed class GetInvestmentProjectsQueryHandler
    : IRequestHandler<GetInvestmentProjectsQuery, PagedResult<InvestmentProjectListDto>>
{
    private readonly IInvestmentProjectRepository _projects;

    public GetInvestmentProjectsQueryHandler(IInvestmentProjectRepository projects) => _projects = projects;

    public Task<PagedResult<InvestmentProjectListDto>> Handle(GetInvestmentProjectsQuery request, CancellationToken ct) =>
        _projects.GetPublishedListAsync(request.Filter, ct);
}
