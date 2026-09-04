using MediatR;

namespace PropertyApi.Application.Investments.Commands.ApproveInvestmentProject;

public sealed record ApproveInvestmentProjectCommand(Guid Id) : IRequest;
