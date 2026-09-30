using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Commands.RemoveInvestmentFromWatchlist;

public sealed record RemoveInvestmentFromWatchlistCommand(
    Guid UserId,
    Guid InvestmentProjectId) : IRequest<InvestmentWatchlistMutationResult>;
