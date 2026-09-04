using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Commands.AddInvestmentToWatchlist;

public sealed class AddInvestmentToWatchlistCommandHandler
    : IRequestHandler<AddInvestmentToWatchlistCommand, InvestmentWatchlistMutationResult>
{
    private readonly IInvestmentWatchlistRepository _watchlist;
    private readonly IUnitOfWork _uow;

    public AddInvestmentToWatchlistCommandHandler(IInvestmentWatchlistRepository watchlist, IUnitOfWork uow)
    {
        _watchlist = watchlist;
        _uow = uow;
    }

    public async Task<InvestmentWatchlistMutationResult> Handle(
        AddInvestmentToWatchlistCommand request,
        CancellationToken ct)
    {
        var projectExists = await _watchlist.ProjectExistsAsync(request.InvestmentProjectId, ct);
        if (!projectExists)
            return InvestmentWatchlistMutationResult.NotFound("Investment project not found.");

        var alreadyExists = await _watchlist.ExistsAsync(request.UserId, request.InvestmentProjectId, ct);
        if (alreadyExists)
            return InvestmentWatchlistMutationResult.Conflict("Project already in watchlist.");

        _watchlist.Add(InvestmentWatchlistItem.Create(request.UserId, request.InvestmentProjectId));
        await _uow.SaveChangesAsync(ct);

        return InvestmentWatchlistMutationResult.Success("Added to watchlist.");
    }
}
