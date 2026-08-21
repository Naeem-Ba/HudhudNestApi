namespace PropertyApi.Application.Common.DTOs;

/// <summary>
/// Shared DTO for Governorate / District / Neighborhood lookups. All three
/// entities share the same shape (Id + bilingual name, optionally scoped to
/// a parent), so one DTO covers all three instead of three near-identical
/// records.
/// </summary>
public sealed record StructuredLocationLookupDto(
    int Id,
    string NameAr,
    string NameEn,
    int? ParentId = null);
