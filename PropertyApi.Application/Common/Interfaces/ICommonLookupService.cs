using PropertyApi.Application.Common.DTOs;

namespace PropertyApi.Application.Common.Interfaces;

public interface ICommonLookupService
{
    Task<IReadOnlyList<AmenityLookupDto>> GetAmenitiesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetPropertyTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetCitiesAsync(CancellationToken ct = default);

    Task RefreshAsync(string key, CancellationToken ct = default);
    Task RefreshAllAsync(CancellationToken ct = default);
}
