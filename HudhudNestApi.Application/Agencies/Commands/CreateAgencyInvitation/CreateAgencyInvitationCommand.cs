using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;

namespace HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitation;

/// <summary>
/// Agency owner invites an existing user to join their agency. Creates a Pending
/// AgencyInvitation only — see the type's doc comment. Replaces the old
/// AddAgencyMemberCommand, which attached the user directly with no consent step
/// (B-2, RELEASE-BLOCKERS-AR.md).
/// </summary>
public sealed record CreateAgencyInvitationCommand(
    Guid AgencyId,
    Guid TargetUserId,
    Guid RequestingUserId,
    string? IpAddress) : IRequest<AgencyInvitationDto>;
