using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Listings.Interfaces;

public interface IPropertyGeoSearchRepository
{
    Task<PagedResult<GeoPropertySearchResultDto>> SearchNearbyAsync(
        GeoPropertySearchRequestDto filter,
        CancellationToken ct = default);
}

