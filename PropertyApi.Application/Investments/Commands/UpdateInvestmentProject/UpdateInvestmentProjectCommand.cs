using MediatR;
using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.Commands.UpdateInvestmentProject;

public sealed record UpdateInvestmentProjectCommand(
    Guid Id,
    string Title,
    string? ShortDescription,
    string Description,
    InvestmentProjectType ProjectType,
    decimal TargetAmount,
    decimal MinimumInvestment,
    decimal? MaximumInvestment,
    string Currency,
    int InvestmentTermMonths,
    decimal ExpectedReturnMin,
    decimal ExpectedReturnMax,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal RaisedAmount) : IRequest;
