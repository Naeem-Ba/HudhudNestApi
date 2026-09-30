using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.PublishInvestmentProject;

public sealed record PublishInvestmentProjectCommand(Guid Id) : IRequest;
