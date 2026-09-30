using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;

namespace HudhudNestApi.Application.Agencies.Queries.GetMyAgencyInvitations;

/// <summary>The caller's own pending, actionable agency invitations.</summary>
public sealed record GetMyAgencyInvitationsQuery(Guid RequestingUserId)
    : IRequest<IReadOnlyList<AgencyInvitationDto>>;
