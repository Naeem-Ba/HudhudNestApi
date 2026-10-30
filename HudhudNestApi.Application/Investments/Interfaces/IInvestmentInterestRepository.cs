using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Interfaces;

public interface IInvestmentInterestRepository
{
    Task<InvestmentInterest?> GetAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default);
    Task<bool> ProjectExistsAndPublishedAsync(Guid investmentProjectId, CancellationToken ct = default);
    void Add(InvestmentInterest interest);
    Task<IReadOnlyList<InvestmentInterestDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Staff view: everyone who expressed interest in the project (any status), newest first.</summary>
    Task<IReadOnlyList<AdminInvestmentInterestDto>> GetForProjectAsync(Guid investmentProjectId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid investmentProjectId, CancellationToken ct = default);

    /// <summary>Ids of the active admin accounts — the recipients of the new-interest notice.</summary>
    Task<IReadOnlyList<Guid>> ListAdminIdsAsync(CancellationToken ct = default);
}
