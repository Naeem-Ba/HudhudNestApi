using MediatR;
using HudhudNestApi.Application.Investments.DTOs;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjectById;

public sealed record GetInvestmentProjectByIdQuery(Guid Id) : IRequest<InvestmentProjectDetailsDto?>;
