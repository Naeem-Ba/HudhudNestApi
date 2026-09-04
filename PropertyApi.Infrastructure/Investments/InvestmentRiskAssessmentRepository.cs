using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Investments;

public sealed class InvestmentRiskAssessmentRepository : IInvestmentRiskAssessmentRepository
{
    private readonly AppDbContext _db;

    public InvestmentRiskAssessmentRepository(AppDbContext db) => _db = db;

    public Task<InvestmentRiskAssessment?> GetByProjectIdAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentRiskAssessments.FirstOrDefaultAsync(r => r.InvestmentProjectId == investmentProjectId, ct);

    public void Add(InvestmentRiskAssessment assessment) => _db.InvestmentRiskAssessments.Add(assessment);

    /// <summary>Published-only gate — same reasoning as InvestmentProjectFinancialsRepository.GetSummaryAsync.</summary>
    public Task<InvestmentRiskDto?> GetDtoAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        (from risk in _db.InvestmentRiskAssessments.AsNoTracking()
         join project in _db.InvestmentProjects.AsNoTracking() on risk.InvestmentProjectId equals project.Id
         where risk.InvestmentProjectId == investmentProjectId && project.Status == InvestmentProjectStatus.Published
         select new InvestmentRiskDto(
             risk.InvestmentProjectId,
             risk.RiskLevel,
             risk.MarketRisk,
             risk.LiquidityRisk,
             risk.ProjectRisk,
             risk.FinancingRisk,
             risk.DeveloperRisk,
             risk.RiskScore,
             risk.RiskSummary))
        .FirstOrDefaultAsync(ct);
}
