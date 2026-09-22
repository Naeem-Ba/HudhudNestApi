using MediatR;

namespace HudhudNestApi.Application.AppUpdates.Commands.DisableAppRelease;

public sealed record DisableAppReleaseCommand(Guid Id, Guid PerformedByUserId, string? IpAddress) : IRequest;
