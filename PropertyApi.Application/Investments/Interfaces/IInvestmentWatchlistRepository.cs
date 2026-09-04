using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Interfaces;

public interface IInvestmentWatchlistRepository
{
    Task<bool> ExistsAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default);
    Task<InvestmentWatchlistItem?> GetAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default);
    Task<bool> ProjectExistsAsync(Guid investmentProjectId, CancellationToken ct = default);
    void Add(InvestmentWatchlistItem item);
    void Remove(InvestmentWatchlistItem item);
    Task<IReadOnlyList<InvestmentWatchlistDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
}
