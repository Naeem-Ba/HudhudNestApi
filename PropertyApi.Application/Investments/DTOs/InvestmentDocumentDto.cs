using PropertyApi.Domain.Investments.Enums;

namespace PropertyApi.Application.Investments.DTOs;

/// <summary>Public/authenticated document view. Deliberately excludes StorageProvider,
/// StorageKey, and DocumentHash — internal, never sent to a client (Phase 1 spec §20).</summary>
public sealed record InvestmentDocumentDto(
    Guid Id,
    InvestmentDocumentType DocumentType,
    string FileName,
    int Version,
    DateTime? PublishedAt,
    string Url);
