using MediatR;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Listings.Queries.SearchPropertiesNearby;

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

