using MediatR;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Commands.CreateInvestmentProject;

public sealed record CreateInvestmentProjectCommand(
    Guid PropertyId,
    Guid OwnerUserId,
    string Title,
    string Description,
    InvestmentProjectType ProjectType,
    string Currency) : IRequest<Guid>;
