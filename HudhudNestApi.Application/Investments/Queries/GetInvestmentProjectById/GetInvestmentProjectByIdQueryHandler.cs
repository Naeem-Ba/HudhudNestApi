using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectById;

/// <summary>Returns null for anything not Published — a draft/under-review project is invisible
/// to a non-admin caller, never a 403 that would leak its existence.</summary>
public sealed class GetInvestmentProjectByIdQueryHandler
    : IRequestHandler<GetInvestmentProjectByIdQuery, InvestmentProjectDetailsDto?>
{
    private readonly IInvestmentProjectRepository _projects;

    public GetInvestmentProjectByIdQueryHandler(IInvestmentProjectRepository projects) => _projects = projects;

    public Task<InvestmentProjectDetailsDto?> Handle(GetInvestmentProjectByIdQuery request, CancellationToken ct) =>
        _projects.GetPublishedDetailsAsync(request.Id, ct);
}
