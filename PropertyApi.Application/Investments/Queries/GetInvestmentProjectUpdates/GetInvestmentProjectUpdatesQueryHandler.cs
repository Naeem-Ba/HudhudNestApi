using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjectUpdates;

public sealed class GetInvestmentProjectUpdatesQueryHandler
    : IRequestHandler<GetInvestmentProjectUpdatesQuery, IReadOnlyList<InvestmentUpdateDto>>
{
    private readonly IInvestmentUpdateRepository _updates;

    public GetInvestmentProjectUpdatesQueryHandler(IInvestmentUpdateRepository updates) => _updates = updates;

    public Task<IReadOnlyList<InvestmentUpdateDto>> Handle(GetInvestmentProjectUpdatesQuery request, CancellationToken ct) =>
        _updates.GetForProjectAsync(request.InvestmentProjectId, ct);
}
