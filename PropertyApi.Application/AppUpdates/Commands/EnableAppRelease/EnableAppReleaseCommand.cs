using MediatR;

namespace PropertyApi.Application.AppUpdates.Commands.EnableAppRelease;

public sealed record EnableAppReleaseCommand(Guid Id, Guid PerformedByUserId, string? IpAddress) : IRequest;
