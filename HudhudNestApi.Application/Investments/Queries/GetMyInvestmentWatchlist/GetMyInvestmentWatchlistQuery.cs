using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetMyInvestmentWatchlist;

public sealed record GetMyInvestmentWatchlistQuery(Guid UserId) : IRequest<IReadOnlyList<InvestmentWatchlistDto>>;
