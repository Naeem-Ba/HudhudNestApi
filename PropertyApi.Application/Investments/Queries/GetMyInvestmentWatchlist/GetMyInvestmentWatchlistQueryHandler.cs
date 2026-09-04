using MediatR;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Queries.GetMyInvestmentWatchlist;

public sealed class GetMyInvestmentWatchlistQueryHandler
    : IRequestHandler<GetMyInvestmentWatchlistQuery, IReadOnlyList<InvestmentWatchlistDto>>
{
    private readonly IInvestmentWatchlistRepository _watchlist;

    public GetMyInvestmentWatchlistQueryHandler(IInvestmentWatchlistRepository watchlist) => _watchlist = watchlist;

    public Task<IReadOnlyList<InvestmentWatchlistDto>> Handle(GetMyInvestmentWatchlistQuery request, CancellationToken ct) =>
        _watchlist.GetByUserIdAsync(request.UserId, ct);
}
