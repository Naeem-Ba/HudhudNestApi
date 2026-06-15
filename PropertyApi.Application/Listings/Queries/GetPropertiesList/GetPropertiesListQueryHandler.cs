using MediatR;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Queries.GetPropertyById;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Listings.Queries.GetPropertiesList;

/// <summary>
/// BUG FIX: Was an empty "internal class GetPropertiesListQueryHandler {}" — completely missing.
///
/// NOTE: Uses GetPropertyByIdQueryHandler.MapToDto() to avoid duplicating mapping logic.
/// If you later add AutoMapper or Mapster, replace the MapToDto call.
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

        return new PagedResult<PropertyDto>
        {
            Items = pagedProperties.Items
                            .Select(GetPropertyByIdQueryHandler.MapToDto)
                            .ToList()
                            .AsReadOnly(),
            TotalCount = pagedProperties.TotalCount,
            Page = pagedProperties.Page,
            PageSize = pagedProperties.PageSize
        };
    }
}

