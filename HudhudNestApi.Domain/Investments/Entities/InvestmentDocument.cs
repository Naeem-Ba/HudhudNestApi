using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Domain.Investments.Entities;

/// <summary>
/// Metadata for one document attached to an <see cref="InvestmentProject"/>. Binary content
/// lives in the existing media storage provider (Cloudinary, via IMediaStorageService) — never
/// in PostgreSQL (Phase 1 spec §9). Only <see cref="IsPublic"/> documents on a
/// <c>Published</c> project are ever returned to non-admin callers; see
/// GetInvestmentProjectDocumentsQueryHandler for the actual gate.
/// </summary>
public sealed class InvestmentDocument : AuditableEntity
{
    public Guid InvestmentProjectId { get; private set; }
    public InvestmentDocumentType DocumentType { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string StorageProvider { get; private set; } = string.Empty;

    /// <summary>Provider-specific identifier (e.g. Cloudinary PublicId) needed to delete the
    /// asset later. Internal — never serialized to a public/authenticated DTO.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>The URL the current storage provider returns for this asset. Reused as-is from
    /// IMediaStorageService's MediaUploadResult — this codebase has no signed/temporary-URL
    /// mechanism yet, so document privacy is enforced by never including a non-public document's
    /// URL in a response, rather than by expiry (see class-level remark).</summary>
    public string Url { get; private set; } = string.Empty;

    public int Version { get; private set; } = 1;
    public string? DocumentHash { get; private set; }
    public bool IsPublic { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    private InvestmentDocument() { }

    public static InvestmentDocument Create(
        Guid investmentProjectId,
        InvestmentDocumentType documentType,
        string fileName,
        string storageProvider,
        string storageKey,
        string url,
        string? documentHash,
        bool isPublic)
    {
        if (investmentProjectId == Guid.Empty)
            throw new DomainException("مشروع الاستثمار مطلوب.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new DomainException("اسم الملف مطلوب.");
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new DomainException("معرّف التخزين مطلوب.");
        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("رابط الملف مطلوب.");

        var document = new InvestmentDocument
        {
            InvestmentProjectId = investmentProjectId,
            DocumentType = documentType,
            FileName = fileName.Trim(),
            StorageProvider = storageProvider.Trim(),
            StorageKey = storageKey.Trim(),
            Url = url.Trim(),
            DocumentHash = string.IsNullOrWhiteSpace(documentHash) ? null : documentHash.Trim(),
            Version = 1,
        };

        if (isPublic)
            document.Publish();

        return document;
    }

    public void Publish()
    {
        IsPublic = true;
        PublishedAt = DateTime.UtcNow;
    }

    public void Unpublish() => IsPublic = false;

    /// <summary>Marks the document removed (soft delete via IsDeleted). The actual provider
    /// asset deletion is the application handler's job, via IMediaStorageService.</summary>
    public void Remove(Guid removedByUserId)
    {
        IsPublic = false;
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedByUserId = removedByUserId;
    }
}
