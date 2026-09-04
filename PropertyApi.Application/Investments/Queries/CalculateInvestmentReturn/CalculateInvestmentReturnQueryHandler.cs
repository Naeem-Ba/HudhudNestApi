using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Application.Investments.Services;

namespace PropertyApi.Application.Investments.Queries.CalculateInvestmentReturn;

public sealed class CalculateInvestmentReturnQueryHandler
    : IRequestHandler<CalculateInvestmentReturnQuery, InvestmentCalculatorResultDto>
{
    private readonly IInvestmentProjectRepository _projects;

    public CalculateInvestmentReturnQueryHandler(IInvestmentProjectRepository projects) => _projects = projects;

    public async Task<InvestmentCalculatorResultDto> Handle(CalculateInvestmentReturnQuery request, CancellationToken ct)
    {
        var project = await _projects.GetPublishedDetailsAsync(request.InvestmentProjectId, ct)
            ?? throw new NotFoundException($"Investment project {request.InvestmentProjectId} was not found.");

        var termMonths = request.TermMonths ?? project.InvestmentTermMonths;

        return InvestmentCalculator.Calculate(
            project.Id,
            request.Amount,
            termMonths,
            project.Currency,
            project.ExpectedReturnMin,
            project.ExpectedReturnMax);
    }
}
