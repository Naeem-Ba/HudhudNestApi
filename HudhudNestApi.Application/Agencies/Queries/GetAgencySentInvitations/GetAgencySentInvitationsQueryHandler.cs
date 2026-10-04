using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;

namespace HudhudNestApi.Application.Agencies.Queries.GetAgencySentInvitations;

public sealed class GetAgencySentInvitationsQueryHandler
    : IRequestHandler<GetAgencySentInvitationsQuery, IReadOnlyList<AgencySentInvitationDto>>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyInvitationRepository _invitations;

    public GetAgencySentInvitationsQueryHandler(
        IAgencyRepository agencies,
        IAgencyInvitationRepository invitations)
    {
        _agencies = agencies;
        _invitations = invitations;
    }

    public async Task<IReadOnlyList<AgencySentInvitationDto>> Handle(
        GetAgencySentInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            throw new NotFoundException("Agency was not found.");

        if (agency.OwnerUserId != request.RequestingUserId)
            throw new ForbiddenException("Only the agency owner can see its invitations.");

        var invitations = await _invitations.GetActionableForAgencyAsync(agency.Id, cancellationToken);

        // At most Agency.MaxMembers-ish rows in practice, so one lookup per row is fine —
        // same shape as GetMyAgencyInvitationsQueryHandler.
        var dtos = new List<AgencySentInvitationDto>(invitations.Count);

        foreach (var invitation in invitations)
        {
            var target = await _agencies.GetUserAccountAsync(invitation.TargetUserId, cancellationToken);

            if (target is null || target.IsDeleted)
                continue;

            dtos.Add(new AgencySentInvitationDto(
                Id: invitation.Id,
                TargetUserId: target.Id,
                TargetName: string.IsNullOrWhiteSpace(target.DisplayName)
                    ? $"{target.FirstName} {target.LastName}".Trim()
                    : target.DisplayName!,
                TargetProfileImageUrl: target.ProfileImageUrl,
                CreatedAt: invitation.CreatedAt,
                ExpiresAt: invitation.ExpiresAt));
        }

        return dtos;
    }
}
