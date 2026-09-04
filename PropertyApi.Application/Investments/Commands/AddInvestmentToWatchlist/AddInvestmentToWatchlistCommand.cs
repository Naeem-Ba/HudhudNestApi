using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Commands.AddInvestmentToWatchlist;

public sealed record AddInvestmentToWatchlistCommand(
    Guid UserId,
    Guid InvestmentProjectId) : IRequest<InvestmentWatchlistMutationResult>;
