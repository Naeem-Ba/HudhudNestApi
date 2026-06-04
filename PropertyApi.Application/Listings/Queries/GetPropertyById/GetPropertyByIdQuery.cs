using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Queries.GetPropertyById;

/// <summary>
/// Returns full property details including images and amenities.
/// Returns null (? 404) if not found or soft-deleted.
///
/// BUG FIX: Was an empty "internal class GetPropertyByIdQuery {}" — completely missing.
/// </summary>
public sealed record GetPropertyByIdQuery(Guid Id) : IRequest<PropertyDto?>;
