using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectUpdates;

public sealed record GetInvestmentProjectUpdatesQuery(Guid InvestmentProjectId)
    : IRequest<IReadOnlyList<InvestmentUpdateDto>>;
