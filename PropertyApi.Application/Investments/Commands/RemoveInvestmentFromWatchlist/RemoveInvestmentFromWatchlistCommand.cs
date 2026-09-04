using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Commands.RemoveInvestmentFromWatchlist;

public sealed record RemoveInvestmentFromWatchlistCommand(
    Guid UserId,
    Guid InvestmentProjectId) : IRequest<InvestmentWatchlistMutationResult>;
