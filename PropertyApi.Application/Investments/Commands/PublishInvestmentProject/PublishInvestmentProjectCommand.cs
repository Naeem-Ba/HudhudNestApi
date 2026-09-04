using MediatR;

namespace PropertyApi.Application.Investments.Commands.PublishInvestmentProject;

public sealed record PublishInvestmentProjectCommand(Guid Id) : IRequest;
