using MediatR;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Listings.Queries.SearchPropertiesNearby;

public sealed record SearchPropertiesNearbyQuery(
    GeoPropertySearchRequestDto Filter
) : IRequest<PagedResult<GeoPropertySearchResultDto>>;

