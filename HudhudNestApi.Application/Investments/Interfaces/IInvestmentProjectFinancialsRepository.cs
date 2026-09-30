using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Interfaces;

public interface IInvestmentProjectFinancialsRepository
{
    Task<InvestmentProjectFinancials?> GetByProjectIdAsync(Guid investmentProjectId, CancellationToken ct = default);
    void Add(InvestmentProjectFinancials financials);
    Task<InvestmentFinancialSummaryDto?> GetSummaryAsync(Guid investmentProjectId, CancellationToken ct = default);
}
