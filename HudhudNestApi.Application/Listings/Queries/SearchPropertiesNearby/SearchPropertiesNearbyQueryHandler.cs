using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Listings.Queries.SearchPropertiesNearby;

public sealed class SearchPropertiesNearbyQueryHandler
    : IRequestHandler<SearchPropertiesNearbyQuery, PagedResult<GeoPropertySearchResultDto>>
{
    private readonly IPropertyGeoSearchRepository _repository;

    public SearchPropertiesNearbyQueryHandler(IPropertyGeoSearchRepository repository)
    {
        _repository = repository;
    }

    public Task<PagedResult<GeoPropertySearchResultDto>> Handle(
        SearchPropertiesNearbyQuery request,
        CancellationToken cancellationToken)
    {
        return _repository.SearchNearbyAsync(request.Filter, cancellationToken);
    }
}

