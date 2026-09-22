using MediatR;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectUpdates;

public sealed class GetInvestmentProjectUpdatesQueryHandler
    : IRequestHandler<GetInvestmentProjectUpdatesQuery, IReadOnlyList<InvestmentUpdateDto>>
{
    private readonly IInvestmentUpdateRepository _updates;

    public GetInvestmentProjectUpdatesQueryHandler(IInvestmentUpdateRepository updates) => _updates = updates;

    public Task<IReadOnlyList<InvestmentUpdateDto>> Handle(GetInvestmentProjectUpdatesQuery request, CancellationToken ct) =>
        _updates.GetForProjectAsync(request.InvestmentProjectId, ct);
}
