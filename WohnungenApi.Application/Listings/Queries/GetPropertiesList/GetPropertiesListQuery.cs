using MediatR;
using WohnungenApi.Application.Listings.DTOs;
using WohnungenApi.Application.Properties.DTOs;

namespace WohnungenApi.Application.Listings.Queries.GetPropertiesList;

/// <summary>
/// Returns a paginated, filtered list of properties.
/// BUG FIX: Was an empty "internal class GetPropertiesListQuery {}" — completely missing.
/// </summary>
public sealed record GetPropertiesListQuery(
    PropertyFilterDto Filter
) : IRequest<PagedResult<PropertyDto>>;