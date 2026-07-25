using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Queries.GetMyProperties;

public sealed record GetMyPropertiesQuery(Guid OwnerId)
    : IRequest<IReadOnlyList<PropertyDto>>;
