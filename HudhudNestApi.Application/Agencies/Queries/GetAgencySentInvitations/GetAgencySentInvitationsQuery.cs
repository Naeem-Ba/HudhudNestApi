using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;

namespace HudhudNestApi.Application.Agencies.Queries.GetAgencySentInvitations;

/// <summary>Owner-only: this agency's pending, not-yet-expired invitations.</summary>
public sealed record GetAgencySentInvitationsQuery(Guid AgencyId, Guid RequestingUserId)
    : IRequest<IReadOnlyList<AgencySentInvitationDto>>;
