using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetMyInvestmentInterests;

public sealed record GetMyInvestmentInterestsQuery(Guid UserId) : IRequest<IReadOnlyList<InvestmentInterestDto>>;
