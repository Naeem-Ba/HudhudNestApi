using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjectUpdates;

public sealed record GetInvestmentProjectUpdatesQuery(Guid InvestmentProjectId)
    : IRequest<IReadOnlyList<InvestmentUpdateDto>>;
