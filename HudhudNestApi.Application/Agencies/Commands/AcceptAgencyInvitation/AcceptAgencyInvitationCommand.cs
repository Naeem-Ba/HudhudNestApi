using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;

namespace HudhudNestApi.Application.Agencies.Commands.AcceptAgencyInvitation;

/// <summary>
/// The invited user accepts — the only action that ever creates the membership
/// (B-2, RELEASE-BLOCKERS-AR.md).
/// </summary>
public sealed record AcceptAgencyInvitationCommand(
    Guid InvitationId,
    Guid RequestingUserId,
    string? IpAddress) : IRequest<AgencyMemberDto>;
