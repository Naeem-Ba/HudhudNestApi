using PropertyApi.Application.Common.DTOs;

namespace PropertyApi.Application.Common.Interfaces;

public interface ICommonLookupService
{
    Task<IReadOnlyList<AmenityLookupDto>> GetAmenitiesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetPropertyTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetCitiesAsync(CancellationToken ct = default);

    // Phase-0, Task 0 follow-up: the real, DB-backed structured-location catalog
    // (Governorate/District/Neighborhood/PropertyType) — previously had entities,
    // tables and seed data but no read endpoint at all. See StructuredLocationLookupDto
    // and PropertyTypeLookupDto for why these are separate DTOs from the ones above.
    Task<IReadOnlyList<StructuredLocationLookupDto>> GetGovernoratesAsync(string countryCode = "SY", CancellationToken ct = default);
    Task<IReadOnlyList<StructuredLocationLookupDto>> GetDistrictsAsync(int governorateId, CancellationToken ct = default);
    Task<IReadOnlyList<StructuredLocationLookupDto>> GetNeighborhoodsAsync(int districtId, CancellationToken ct = default);
    Task<IReadOnlyList<PropertyTypeLookupDto>> GetPropertyTypeCatalogAsync(CancellationToken ct = default);

    Task RefreshAsync(string key, CancellationToken ct = default);
    Task RefreshAllAsync(CancellationToken ct = default);
}

