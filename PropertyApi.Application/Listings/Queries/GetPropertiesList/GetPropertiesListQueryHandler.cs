using MediatR;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Mappers;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Listings.Queries.GetPropertiesList;

/// <summary>
/// Public, paginated listing search. Filtering, and the featured-first default ordering,
/// happen in the repository — this handler only shapes the result.
/// </summary>
public sealed class GetPropertiesListQueryHandler
    : IRequestHandler<GetPropertiesListQuery, PagedResult<PropertyDto>>
{
    private readonly IPropertyRepository _repo;

    public GetPropertiesListQueryHandler(IPropertyRepository repo)
        => _repo = repo;

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

        return new PagedResult<PropertyDto>
        {
            Items = pagedProperties.Items
                            .Select(property => PropertyMapper.ToDto(property, asOfUtc))
                            .ToList()
                            .AsReadOnly(),
            TotalCount = pagedProperties.TotalCount,
            Page = pagedProperties.Page,
            PageSize = pagedProperties.PageSize
        };
    }
}
