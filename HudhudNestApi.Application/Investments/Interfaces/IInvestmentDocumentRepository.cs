using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Application.Investments.Interfaces;

public interface IInvestmentDocumentRepository
{
    Task<InvestmentDocument?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(InvestmentDocument document);

    /// <summary>Only IsPublic documents — what a non-admin caller may ever see.</summary>
    Task<IReadOnlyList<InvestmentDocumentDto>> GetPublicDocumentsAsync(Guid investmentProjectId, CancellationToken ct = default);

    Task<IReadOnlyList<InvestmentDocumentDto>> GetAllDocumentsForAdminAsync(Guid investmentProjectId, CancellationToken ct = default);

    Task<int> CountPublicAsync(Guid investmentProjectId, CancellationToken ct = default);
}
