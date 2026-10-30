using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetAdminInvestmentInterests;

public sealed record GetAdminInvestmentInterestsQuery(Guid InvestmentProjectId)
    : IRequest<IReadOnlyList<AdminInvestmentInterestDto>>;
