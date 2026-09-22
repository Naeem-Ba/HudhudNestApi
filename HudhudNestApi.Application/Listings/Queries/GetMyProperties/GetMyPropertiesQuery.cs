using MediatR;
using HudhudNestApi.Application.Listings.DTOs;

namespace HudhudNestApi.Application.Listings.Queries.GetMyProperties;

public sealed record GetMyPropertiesQuery(Guid OwnerId)
    : IRequest<IReadOnlyList<PropertyDto>>;
