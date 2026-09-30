using MediatR;
using HudhudNestApi.Application.Amenities.DTOs;

namespace HudhudNestApi.Application.Amenities.Queries.GetAmenities;

public sealed record GetAmenitiesQuery : IRequest<IReadOnlyList<AmenityDto>>;

