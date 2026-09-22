using MediatR;

namespace HudhudNestApi.Application.AppUpdates.Commands.DeleteAppRelease;

public sealed record DeleteAppReleaseCommand(Guid Id, Guid PerformedByUserId, string? IpAddress) : IRequest;
