using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Investments.DTOs;

/// <summary>Public/authenticated document view. Deliberately excludes StorageProvider,
/// StorageKey, and DocumentHash — internal, never sent to a client (Phase 1 spec §20).</summary>
/// <param name="IsPublic">Defaults to true for older call sites that predate this field (all of
/// which construct only already-public documents); the admin document list is the only caller
/// that needs the real value, and it always passes it explicitly.</param>
public sealed record InvestmentDocumentDto(
    Guid Id,
    InvestmentDocumentType DocumentType,
    string FileName,
    int Version,
    DateTime? PublishedAt,
    string Url,
    bool IsPublic = true);
