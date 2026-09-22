using MediatR;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.Commands.AddInvestmentUpdate;

public sealed record AddInvestmentUpdateCommand(
    Guid InvestmentProjectId,
    string Title,
    string Content,
    InvestmentUpdateType UpdateType,
    Guid CreatedByUserId) : IRequest<Guid>;
