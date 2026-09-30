using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Mapping;

namespace HudhudNestApi.Application.Agencies.Queries.GetMyAgencyInvitations;

/// <summary>
/// Resolves the caller's own invitation inbox — never anyone else's (IAgencyInvitationRepository
/// filters by TargetUserId, and the controller passes the authenticated caller's id, never
/// one from the request body/route).
/// </summary>
public sealed class GetMyAgencyInvitationsQueryHandler
    : IRequestHandler<GetMyAgencyInvitationsQuery, IReadOnlyList<AgencyInvitationDto>>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyInvitationRepository _invitations;

    public GetMyAgencyInvitationsQueryHandler(
        IAgencyRepository agencies,
        IAgencyInvitationRepository invitations)
    {
        _agencies = agencies;
        _invitations = invitations;
    }

    public async Task<IReadOnlyList<AgencyInvitationDto>> Handle(
        GetMyAgencyInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        var invitations = await _invitations.GetActionableForTargetAsync(
            request.RequestingUserId,
            cancellationToken);

        if (invitations.Count == 0)
            return [];

        var dtos = new List<AgencyInvitationDto>(invitations.Count);

        foreach (var invitation in invitations)
        {
            var agency = await _agencies.GetByIdAsync(invitation.AgencyId, cancellationToken);

            if (agency is null || agency.IsDeleted)
                continue;

            var inviter = await _agencies.GetUserAccountAsync(invitation.InviterUserId, cancellationToken);

            if (inviter is null)
                continue;

            dtos.Add(AgencyMapper.ToInvitationDto(invitation, agency, inviter));
        }

        return dtos;
    }
}
