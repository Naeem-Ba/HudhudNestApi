using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Queries.GetPropertyImages;

public sealed record GetPropertyImagesQuery(Guid PropertyId) : IRequest<PropertyImagesQueryResult>;

