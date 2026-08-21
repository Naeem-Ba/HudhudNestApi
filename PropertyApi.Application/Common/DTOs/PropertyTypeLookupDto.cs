namespace PropertyApi.Application.Common.DTOs;

/// <summary>
/// The real, DB-backed PropertyType catalog (id + Code + bilingual name +
/// Category + Icon). Deliberately a separate DTO/endpoint from the existing
/// "property-types" lookup, which returns a hardcoded 5-item catalog that
/// does NOT correspond to the PropertyType table or to Property.PropertyTypeId —
/// see CommonLookupService.GetPropertyTypesAsync for that older, unrelated one.
/// </summary>
public sealed record PropertyTypeLookupDto(
    int Id,
    string Code,
    string NameAr,
    string NameEn,
    string Category,
    string? Icon);
