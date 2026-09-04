using MediatR;
using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjectById;

public sealed record GetInvestmentProjectByIdQuery(Guid Id) : IRequest<InvestmentProjectDetailsDto?>;
