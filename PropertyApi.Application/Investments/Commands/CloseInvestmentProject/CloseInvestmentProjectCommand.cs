using MediatR;

namespace PropertyApi.Application.Investments.Commands.CloseInvestmentProject;

public sealed record CloseInvestmentProjectCommand(Guid Id) : IRequest;
