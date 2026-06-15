using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Listings.Interfaces;

public interface IPropertyGeoSearchRepository
{
    Task<PagedResult<GeoPropertySearchResultDto>> SearchNearbyAsync(
        GeoPropertySearchRequestDto filter,
        CancellationToken ct = default);
}

