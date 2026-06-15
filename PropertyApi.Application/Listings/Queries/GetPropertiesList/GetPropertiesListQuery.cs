using MediatR;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Listings.Queries.GetPropertiesList;

/// <summary>
/// Returns a paginated, filtered list of properties.
/// BUG FIX: Was an empty "internal class GetPropertiesListQuery {}" — completely missing.
/// </summary>
public sealed record GetPropertiesListQuery(
    PropertyFilterDto Filter
) : IRequest<PagedResult<PropertyDto>>;

