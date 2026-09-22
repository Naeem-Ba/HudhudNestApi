using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;

namespace HudhudNestApi.Application.Investments.Commands.RemoveInvestmentFromWatchlist;

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
