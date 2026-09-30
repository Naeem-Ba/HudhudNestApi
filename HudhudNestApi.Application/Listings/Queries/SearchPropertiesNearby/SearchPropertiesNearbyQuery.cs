using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Listings.Queries.SearchPropertiesNearby;

public sealed record SearchPropertiesNearbyQuery(
    GeoPropertySearchRequestDto Filter
) : IRequest<PagedResult<GeoPropertySearchResultDto>>;

