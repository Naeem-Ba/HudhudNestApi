using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Interfaces;

public interface IInvestmentInterestRepository
{
    Task<InvestmentInterest?> GetAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default);
    Task<bool> ProjectExistsAndPublishedAsync(Guid investmentProjectId, CancellationToken ct = default);
    void Add(InvestmentInterest interest);
    Task<IReadOnlyList<InvestmentInterestDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
}
