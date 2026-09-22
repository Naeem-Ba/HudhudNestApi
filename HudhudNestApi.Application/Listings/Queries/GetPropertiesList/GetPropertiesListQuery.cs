using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertiesList;

/// <summary>
/// Returns a paginated, filtered list of properties.
/// BUG FIX: Was an empty "internal class GetPropertiesListQuery {}" — completely missing.
/// </summary>
public sealed record GetPropertiesListQuery(
    PropertyFilterDto Filter
) : IRequest<PagedResult<PropertyDto>>;

