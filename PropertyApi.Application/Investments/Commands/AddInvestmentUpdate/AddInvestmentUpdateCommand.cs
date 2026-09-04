using MediatR;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Commands.AddInvestmentUpdate;

public sealed record AddInvestmentUpdateCommand(
    Guid InvestmentProjectId,
    string Title,
    string Content,
    InvestmentUpdateType UpdateType,
    Guid CreatedByUserId) : IRequest<Guid>;
