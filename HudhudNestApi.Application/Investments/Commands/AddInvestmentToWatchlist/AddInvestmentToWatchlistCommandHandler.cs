using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Commands.AddInvestmentToWatchlist;

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
        // Same rule as expressing interest: an unpublished project is not public, so it answers
        // exactly like a missing one (otherwise the watchlist would confirm its id exists).
        var projectPublished = await _watchlist.ProjectExistsAndPublishedAsync(request.InvestmentProjectId, ct);
        if (!projectPublished)
            return InvestmentWatchlistMutationResult.NotFound("Investment project not found or not published.");

        var alreadyExists = await _watchlist.ExistsAsync(request.UserId, request.InvestmentProjectId, ct);
        if (alreadyExists)
            return InvestmentWatchlistMutationResult.Conflict("Project already in watchlist.");

        _watchlist.Add(InvestmentWatchlistItem.Create(request.UserId, request.InvestmentProjectId));
        await _uow.SaveChangesAsync(ct);

        return InvestmentWatchlistMutationResult.Success("Added to watchlist.");
    }
}
