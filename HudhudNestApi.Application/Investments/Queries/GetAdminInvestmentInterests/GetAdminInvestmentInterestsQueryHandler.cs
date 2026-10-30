using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Queries.GetAdminInvestmentInterests;

public sealed class GetAdminInvestmentInterestsQueryHandler
    : IRequestHandler<GetAdminInvestmentInterestsQuery, IReadOnlyList<AdminInvestmentInterestDto>>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IInvestmentInterestRepository _interests;

    public GetAdminInvestmentInterestsQueryHandler(
        IInvestmentProjectRepository projects, IInvestmentInterestRepository interests)
    {
        _projects = projects;
        _interests = interests;
    }

    public async Task<IReadOnlyList<AdminInvestmentInterestDto>> Handle(
        GetAdminInvestmentInterestsQuery request, CancellationToken ct)
    {
        _ = await _projects.GetByIdAsync(request.InvestmentProjectId, ct)
            ?? throw new NotFoundException($"Investment project {request.InvestmentProjectId} was not found.");

        return await _interests.GetForProjectAsync(request.InvestmentProjectId, ct);
    }
}
