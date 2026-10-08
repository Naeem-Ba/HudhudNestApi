using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Mappers;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Reviews.Interfaces;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertiesList;

/// <summary>
/// Public, paginated listing search. Filtering, and the featured-first default ordering,
/// happen in the repository — this handler only shapes the result.
/// </summary>
public sealed class GetPropertiesListQueryHandler
    : IRequestHandler<GetPropertiesListQuery, PagedResult<PropertyDto>>
{
    private readonly IPropertyRepository _repo;
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyRatingRepository _agencyRatings;

    public GetPropertiesListQueryHandler(
        IPropertyRepository repo,
        IAgencyRepository agencies,
        IAgencyRatingRepository agencyRatings)
    {
        _repo = repo;
        _agencies = agencies;
        _agencyRatings = agencyRatings;
    }

    public async Task<PagedResult<PropertyDto>> Handle(
        GetPropertiesListQuery request,
        CancellationToken cancellationToken)
    {
        var pagedProperties = await _repo.GetPagedAsync(
            request.Filter,
            cancellationToken);

        // One clock reading for the whole page: two listings whose featured windows straddle
        // "now" must not be judged against two different instants within one response.
        var asOfUtc = DateTime.UtcNow;

        var items = pagedProperties.Items
            .Select(property => PropertyMapper.ToDto(property, asOfUtc))
            .ToList();

        await AgencyListingInfoEnricher.EnrichAsync(items, _agencies, _agencyRatings, cancellationToken);

        return new PagedResult<PropertyDto>
        {
            Items = items.AsReadOnly(),
            TotalCount = pagedProperties.TotalCount,
            Page = pagedProperties.Page,
            PageSize = pagedProperties.PageSize
        };
    }
}
