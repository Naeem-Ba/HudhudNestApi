using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Interfaces;

public interface IInvestmentUpdateRepository
{
    void Add(InvestmentUpdate update);
    Task<IReadOnlyList<InvestmentUpdateDto>> GetForProjectAsync(Guid investmentProjectId, CancellationToken ct = default);
}
