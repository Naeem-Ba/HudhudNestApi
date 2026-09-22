using MediatR;
using HudhudNestApi.Application.Listings.DTOs;

namespace HudhudNestApi.Application.Listings.Commands.DeletePropertyImage;

public sealed record DeletePropertyImageCommand(
    Guid PropertyId,
    Guid ImageId,
    Guid UserId) : IRequest<PropertyImageMutationResult>;

