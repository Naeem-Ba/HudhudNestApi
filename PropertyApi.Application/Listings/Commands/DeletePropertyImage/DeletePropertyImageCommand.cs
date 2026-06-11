using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Commands.DeletePropertyImage;

public sealed record DeletePropertyImageCommand(
    Guid PropertyId,
    Guid ImageId,
    Guid UserId) : IRequest<PropertyImageMutationResult>;
