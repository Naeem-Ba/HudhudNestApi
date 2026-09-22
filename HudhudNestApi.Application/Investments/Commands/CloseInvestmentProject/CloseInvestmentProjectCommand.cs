using MediatR;

namespace HudhudNestApi.Application.Investments.Commands.CloseInvestmentProject;

public sealed record CloseInvestmentProjectCommand(Guid Id) : IRequest;
