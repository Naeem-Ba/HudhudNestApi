using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetMyInvestmentWatchlist;

public sealed record GetMyInvestmentWatchlistQuery(Guid UserId) : IRequest<IReadOnlyList<InvestmentWatchlistDto>>;
