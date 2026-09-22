using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Commands.AddInvestmentToWatchlist;

public sealed record AddInvestmentToWatchlistCommand(
    Guid UserId,
    Guid InvestmentProjectId) : IRequest<InvestmentWatchlistMutationResult>;
