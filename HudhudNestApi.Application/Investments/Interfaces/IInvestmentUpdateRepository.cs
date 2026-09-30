using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Interfaces;

public interface IInvestmentUpdateRepository
{
    void Add(InvestmentUpdate update);
    Task<IReadOnlyList<InvestmentUpdateDto>> GetForProjectAsync(Guid investmentProjectId, CancellationToken ct = default);
}
