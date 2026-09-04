using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Interfaces;

public interface IInvestmentRiskAssessmentRepository
{
    Task<InvestmentRiskAssessment?> GetByProjectIdAsync(Guid investmentProjectId, CancellationToken ct = default);
    void Add(InvestmentRiskAssessment assessment);
    Task<InvestmentRiskDto?> GetDtoAsync(Guid investmentProjectId, CancellationToken ct = default);
}
