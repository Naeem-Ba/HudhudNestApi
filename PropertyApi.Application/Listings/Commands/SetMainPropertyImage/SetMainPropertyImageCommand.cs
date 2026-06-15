using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Commands.SetMainPropertyImage;

public sealed record SetMainPropertyImageCommand(
    Guid PropertyId,
    Guid ImageId,
    Guid UserId) : IRequest<PropertyImageMutationResult>;

