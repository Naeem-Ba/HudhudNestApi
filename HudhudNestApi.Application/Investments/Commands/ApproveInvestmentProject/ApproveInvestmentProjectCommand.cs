using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.ApproveInvestmentProject;

public sealed record ApproveInvestmentProjectCommand(Guid Id) : IRequest;
