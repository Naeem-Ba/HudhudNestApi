using MediatR;

namespace PropertyApi.Application.AppUpdates.Commands.DeleteAppRelease;

public sealed record DeleteAppReleaseCommand(Guid Id, Guid PerformedByUserId, string? IpAddress) : IRequest;
