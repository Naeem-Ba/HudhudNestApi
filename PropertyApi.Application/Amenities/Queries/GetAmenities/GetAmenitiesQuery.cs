using MediatR;
using PropertyApi.Application.Amenities.DTOs;

namespace PropertyApi.Application.Amenities.Queries.GetAmenities;

public sealed record GetAmenitiesQuery : IRequest<IReadOnlyList<AmenityDto>>;
