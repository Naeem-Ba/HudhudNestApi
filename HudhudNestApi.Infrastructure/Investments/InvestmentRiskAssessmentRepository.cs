using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Investments;

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
