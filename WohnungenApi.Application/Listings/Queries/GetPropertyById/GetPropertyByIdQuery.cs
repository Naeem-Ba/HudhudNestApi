using MediatR;
using WohnungenApi.Application.Listings.DTOs;

namespace WohnungenApi.Application.Listings.Queries.GetPropertyById;

/// <summary>
/// Returns full property details including images and amenities.
/// Returns null (→ 404) if not found or soft-deleted.
///
/// BUG FIX: Was an empty "internal class GetPropertyByIdQuery {}" — completely missing.
/// </summary>
public sealed record GetPropertyByIdQuery(Guid Id) : IRequest<PropertyDto?>;