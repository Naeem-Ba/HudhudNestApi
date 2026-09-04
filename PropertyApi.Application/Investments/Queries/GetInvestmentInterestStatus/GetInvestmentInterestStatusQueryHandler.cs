using MediatR;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentInterestStatus;

public sealed class GetInvestmentInterestStatusQueryHandler
    : IRequestHandler<GetInvestmentInterestStatusQuery, InvestmentInterestStatus?>
{
    private readonly IInvestmentInterestRepository _interests;

    public GetInvestmentInterestStatusQueryHandler(IInvestmentInterestRepository interests) => _interests = interests;

    public async Task<InvestmentInterestStatus?> Handle(GetInvestmentInterestStatusQuery request, CancellationToken ct)
    {
        var interest = await _interests.GetAsync(request.UserId, request.InvestmentProjectId, ct);
        return interest?.Status;
    }
}
