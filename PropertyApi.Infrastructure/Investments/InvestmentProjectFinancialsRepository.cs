using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Investments;

public sealed class InvestmentProjectFinancialsRepository : IInvestmentProjectFinancialsRepository
{
    private readonly AppDbContext _db;

    public InvestmentProjectFinancialsRepository(AppDbContext db) => _db = db;

    public Task<InvestmentProjectFinancials?> GetByProjectIdAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentProjectFinancials.FirstOrDefaultAsync(f => f.InvestmentProjectId == investmentProjectId, ct);

    public void Add(InvestmentProjectFinancials financials) => _db.InvestmentProjectFinancials.Add(financials);

    /// <summary>
    /// Only returns a summary for a Published project — an unpublished project's financials
    /// must be exactly as invisible to a public caller as the project itself (Phase 1 spec §19).
    /// </summary>
    public Task<InvestmentFinancialSummaryDto?> GetSummaryAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        (from financials in _db.InvestmentProjectFinancials.AsNoTracking()
         join project in _db.InvestmentProjects.AsNoTracking() on financials.InvestmentProjectId equals project.Id
         join property in _db.Properties.AsNoTracking() on project.PropertyId equals property.Id
         where financials.InvestmentProjectId == investmentProjectId && project.Status == InvestmentProjectStatus.Published
         select new InvestmentFinancialSummaryDto(
             financials.InvestmentProjectId,
             property.PurchasePrice ?? property.ColdRent,
             financials.PurchasePrice,
             financials.RenovationCost,
             financials.ConstructionCost,
             financials.Taxes,
             financials.NotaryCost,
             financials.BrokerCost,
             financials.FinancingCost,
             financials.OperatingCost,
             financials.ContingencyReserve,
             financials.TotalProjectCost,
             financials.ExpectedRevenue,
             financials.ExpectedProfit))
        .FirstOrDefaultAsync(ct);
}
