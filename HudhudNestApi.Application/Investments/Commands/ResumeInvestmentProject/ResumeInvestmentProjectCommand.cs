using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.ResumeInvestmentProject;

public sealed record ResumeInvestmentProjectCommand(Guid Id) : IRequest;
