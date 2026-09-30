using MediatR;

namespace HudhudNestApi.Application.Listings.Commands.PublishProperty;

public sealed record PublishPropertyCommand(
    Guid PropertyId,
    Guid RequestingUserId,
    bool IsAdmin) : IRequest;
