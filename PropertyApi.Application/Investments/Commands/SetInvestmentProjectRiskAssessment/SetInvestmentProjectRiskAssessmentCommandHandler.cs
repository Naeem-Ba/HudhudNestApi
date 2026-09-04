using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Commands.SetInvestmentProjectRiskAssessment;

public sealed class SetInvestmentProjectRiskAssessmentCommandHandler
    : IRequestHandler<SetInvestmentProjectRiskAssessmentCommand>
{
    private readonly IInvestmentProjectRepository _projects;
    private readonly IInvestmentRiskAssessmentRepository _riskAssessments;
    private readonly IUnitOfWork _uow;

    public SetInvestmentProjectRiskAssessmentCommandHandler(
        IInvestmentProjectRepository projects,
        IInvestmentRiskAssessmentRepository riskAssessments,
        IUnitOfWork uow)
    {
        _projects = projects;
        _riskAssessments = riskAssessments;
        _uow = uow;
    }

    public async Task Handle(SetInvestmentProjectRiskAssessmentCommand request, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(request.InvestmentProjectId, ct)
            ?? throw new NotFoundException($"Investment project {request.InvestmentProjectId} was not found.");

        var assessment = await _riskAssessments.GetByProjectIdAsync(request.InvestmentProjectId, ct);
        var isNew = assessment is null;

        if (isNew)
        {
            assessment = InvestmentRiskAssessment.Create(
                request.InvestmentProjectId,
                request.RiskLevel,
                request.MarketRisk,
                request.LiquidityRisk,
                request.ProjectRisk,
                request.FinancingRisk,
                request.DeveloperRisk,
                request.RiskScore,
                request.RiskSummary);

            _riskAssessments.Add(assessment);
        }
        else
        {
            assessment!.Update(
                request.RiskLevel,
                request.MarketRisk,
                request.LiquidityRisk,
                request.ProjectRisk,
                request.FinancingRisk,
                request.DeveloperRisk,
                request.RiskScore,
                request.RiskSummary);
        }

        // Keep the project's denormalized filter field in sync (Phase 1 spec §32).
        project.SetOverallRiskLevel(request.RiskLevel);

        await _uow.SaveChangesAsync(ct);
    }
}
