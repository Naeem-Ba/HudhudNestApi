using MediatR;
using HudhudNestApi.Application.Listings.DTOs;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertyImages;

public sealed record GetPropertyImagesQuery(Guid PropertyId) : IRequest<PropertyImagesQueryResult>;

