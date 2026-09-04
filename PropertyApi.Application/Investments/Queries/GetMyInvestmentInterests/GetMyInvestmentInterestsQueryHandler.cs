using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Queries.GetMyInvestmentInterests;

public sealed class GetMyInvestmentInterestsQueryHandler
    : IRequestHandler<GetMyInvestmentInterestsQuery, IReadOnlyList<InvestmentInterestDto>>
{
    private readonly IInvestmentInterestRepository _interests;

    public GetMyInvestmentInterestsQueryHandler(IInvestmentInterestRepository interests) => _interests = interests;

    public Task<IReadOnlyList<InvestmentInterestDto>> Handle(GetMyInvestmentInterestsQuery request, CancellationToken ct) =>
        _interests.GetByUserIdAsync(request.UserId, ct);
}
