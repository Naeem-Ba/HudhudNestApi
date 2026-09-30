using MediatR;
using HudhudNestApi.Application.Listings.DTOs;

namespace HudhudNestApi.Application.Listings.Commands.SetMainPropertyImage;

public sealed record SetMainPropertyImageCommand(
    Guid PropertyId,
    Guid ImageId,
    Guid UserId) : IRequest<PropertyImageMutationResult>;

