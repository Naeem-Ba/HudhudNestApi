using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Interfaces;

public interface IInvestmentProjectFinancialsRepository
{
    Task<InvestmentProjectFinancials?> GetByProjectIdAsync(Guid investmentProjectId, CancellationToken ct = default);
    void Add(InvestmentProjectFinancials financials);
    Task<InvestmentFinancialSummaryDto?> GetSummaryAsync(Guid investmentProjectId, CancellationToken ct = default);
}
