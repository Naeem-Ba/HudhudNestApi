using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Interfaces;

public interface IInvestmentRiskAssessmentRepository
{
    Task<InvestmentRiskAssessment?> GetByProjectIdAsync(Guid investmentProjectId, CancellationToken ct = default);
    void Add(InvestmentRiskAssessment assessment);
    Task<InvestmentRiskDto?> GetDtoAsync(Guid investmentProjectId, CancellationToken ct = default);
}
