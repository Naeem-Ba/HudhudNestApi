using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.SuspendInvestmentProject;

public sealed record SuspendInvestmentProjectCommand(Guid Id) : IRequest;
