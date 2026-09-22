using MediatR;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.Commands.CreateInvestmentProject;

public sealed record CreateInvestmentProjectCommand(
    Guid PropertyId,
    Guid OwnerUserId,
    string Title,
    string Description,
    InvestmentProjectType ProjectType,
    string Currency) : IRequest<Guid>;
