using MediatR;

namespace PropertyApi.Application.Investments.Commands.RejectInvestmentProject;

public sealed record RejectInvestmentProjectCommand(Guid Id, string Reason) : IRequest;
