using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;

namespace PropertyApi.Application.Investments.Commands.RemoveInvestmentFromWatchlist;

public sealed class RemoveInvestmentFromWatchlistCommandHandler
    : IRequestHandler<RemoveInvestmentFromWatchlistCommand, InvestmentWatchlistMutationResult>
{
    private readonly IInvestmentWatchlistRepository _watchlist;
    private readonly IUnitOfWork _uow;

    public RemoveInvestmentFromWatchlistCommandHandler(IInvestmentWatchlistRepository watchlist, IUnitOfWork uow)
    {
        _watchlist = watchlist;
        _uow = uow;
    }

    public async Task<InvestmentWatchlistMutationResult> Handle(
        RemoveInvestmentFromWatchlistCommand request,
        CancellationToken ct)
    {
        var item = await _watchlist.GetAsync(request.UserId, request.InvestmentProjectId, ct);
        if (item is null)
            return InvestmentWatchlistMutationResult.NotFound();

        _watchlist.Remove(item);
        await _uow.SaveChangesAsync(ct);

        return InvestmentWatchlistMutationResult.Success();
    }
}
