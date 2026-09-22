using MediatR;

namespace HudhudNestApi.Application.AppUpdates.Commands.EnableAppRelease;

public sealed record EnableAppReleaseCommand(Guid Id, Guid PerformedByUserId, string? IpAddress) : IRequest;
