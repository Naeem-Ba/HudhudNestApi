using System.Globalization;
using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Application.Investments.Services;

namespace HudhudNestApi.Application.Investments.Queries.CalculateInvestmentReturn;

public sealed class CalculateInvestmentReturnQueryHandler
    : IRequestHandler<CalculateInvestmentReturnQuery, InvestmentCalculatorResultDto>
{
    private readonly IInvestmentProjectRepository _projects;

    public CalculateInvestmentReturnQueryHandler(IInvestmentProjectRepository projects) => _projects = projects;

    public async Task<InvestmentCalculatorResultDto> Handle(CalculateInvestmentReturnQuery request, CancellationToken ct)
    {
        var project = await _projects.GetPublishedDetailsAsync(request.InvestmentProjectId, ct)
            ?? throw new NotFoundException($"Investment project {request.InvestmentProjectId} was not found.");

        // QA Phase 10, P10-D07 (rest): the estimate is for an amount the project could actually accept, so the
        // project's own minimum/maximum apply. Without this the page showed a "profit" for an amount nobody can invest.
        if (request.Amount < project.MinimumInvestment)
            throw new ValidationException(nameof(request.Amount),
                $"المبلغ أقل من الحد الأدنى للاستثمار في هذا المشروع ({Format(project.MinimumInvestment)} {project.Currency}).");
        if (project.MaximumInvestment is { } max && request.Amount > max)
            throw new ValidationException(nameof(request.Amount),
                $"المبلغ أكبر من الحد الأقصى للاستثمار في هذا المشروع ({Format(max)} {project.Currency}).");

        var termMonths = request.TermMonths ?? project.InvestmentTermMonths;

        return InvestmentCalculator.Calculate(
            project.Id,
            request.Amount,
            termMonths,
            project.Currency,
            project.ExpectedReturnMin,
            project.ExpectedReturnMax);
    }

    private static string Format(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);
}
