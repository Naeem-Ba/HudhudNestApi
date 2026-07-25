using MediatR;

namespace PropertyApi.Application.Listings.Commands.PublishProperty;

public sealed record PublishPropertyCommand(
    Guid PropertyId,
    Guid RequestingUserId,
    bool IsAdmin) : IRequest;
