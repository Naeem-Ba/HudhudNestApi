using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetMyInvestmentInterests;

public sealed record GetMyInvestmentInterestsQuery(Guid UserId) : IRequest<IReadOnlyList<InvestmentInterestDto>>;
