using MediatR;

namespace HudhudNestApi.Application.Agencies.Commands.DeclineAgencyInvitation;

/// <summary>The invited user declines. Never creates a membership.</summary>
public sealed record DeclineAgencyInvitationCommand(
    Guid InvitationId,
    Guid RequestingUserId) : IRequest<Unit>;
