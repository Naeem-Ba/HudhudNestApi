using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Investments;

public sealed class InvestmentDocumentRepository : IInvestmentDocumentRepository
{
    private readonly AppDbContext _db;

    public InvestmentDocumentRepository(AppDbContext db) => _db = db;

    public Task<InvestmentDocument?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.InvestmentDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);

    public void Add(InvestmentDocument document) => _db.InvestmentDocuments.Add(document);

    /// <summary>Published-project + IsPublic gate — never returns a document (or its Url) for a
    /// project that is not Published, or a document not explicitly marked public (Phase 1 §20).</summary>
    public async Task<IReadOnlyList<InvestmentDocumentDto>> GetPublicDocumentsAsync(Guid investmentProjectId, CancellationToken ct = default)
    {
        var query =
            from document in _db.InvestmentDocuments.AsNoTracking()
            join project in _db.InvestmentProjects.AsNoTracking() on document.InvestmentProjectId equals project.Id
            where document.InvestmentProjectId == investmentProjectId &&
                  document.IsPublic &&
                  project.Status == InvestmentProjectStatus.Published
            orderby document.CreatedAt descending
            select new InvestmentDocumentDto(document.Id, document.DocumentType, document.FileName, document.Version, document.PublishedAt, document.Url, document.IsPublic);

        return await query.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InvestmentDocumentDto>> GetAllDocumentsForAdminAsync(Guid investmentProjectId, CancellationToken ct = default)
    {
        return await _db.InvestmentDocuments
            .AsNoTracking()
            .Where(d => d.InvestmentProjectId == investmentProjectId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new InvestmentDocumentDto(d.Id, d.DocumentType, d.FileName, d.Version, d.PublishedAt, d.Url, d.IsPublic))
            .ToListAsync(ct);
    }

    public Task<int> CountPublicAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentDocuments.CountAsync(d => d.InvestmentProjectId == investmentProjectId && d.IsPublic, ct);
}
