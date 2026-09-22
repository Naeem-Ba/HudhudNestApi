using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.RejectInvestmentProject;

public sealed record RejectInvestmentProjectCommand(Guid Id, string Reason) : IRequest;
