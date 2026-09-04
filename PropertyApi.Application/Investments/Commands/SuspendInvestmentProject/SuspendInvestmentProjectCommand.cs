using MediatR;

namespace PropertyApi.Application.Investments.Commands.SuspendInvestmentProject;

public sealed record SuspendInvestmentProjectCommand(Guid Id) : IRequest;
