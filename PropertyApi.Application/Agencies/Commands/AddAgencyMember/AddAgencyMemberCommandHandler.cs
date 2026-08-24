using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Agencies.DTOs;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Agencies.Mapping;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Application.Agencies.Commands.AddAgencyMember;

/// <summary>
/// Attaches a user to an agency and grants them the AgencyAgent role.
///
/// NOTE — this adds someone without asking them. That is a deliberate limit of the current
/// model, not an oversight: there is no invitation entity and no accept/decline flow, so an
/// owner can attach any user id they know. The blast radius is bounded on purpose —
/// membership grants no access to anyone else's listings, and the member can leave at any
/// time — but an invitation flow is the honest next step, and until it exists this endpoint
/// should stay owner-only and audited.
/// </summary>
public sealed class AddAgencyMemberCommandHandler
    : IRequestHandler<AddAgencyMemberCommand, AgencyMemberDto>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAdminIdentityService _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AddAgencyMemberCommandHandler> _logger;

    public AddAgencyMemberCommandHandler(
        IAgencyRepository agencies,
        IAdminIdentityService identity,
        IUnitOfWork unitOfWork,
        ILogger<AddAgencyMemberCommandHandler> logger)
    {
        _agencies = agencies;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AgencyMemberDto> Handle(
        AddAgencyMemberCommand request,
        CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            throw new NotFoundException("Agency was not found.");

        // Holding AgencyOwner is not enough — it must be THIS agency's owner. Without this
        // comparison the role would let any agency owner add members to any agency.
        if (agency.OwnerUserId != request.RequestingUserId)
            throw new ForbiddenException("Only the agency owner can add members.");

        if (!agency.IsActive)
        {
            throw new ConflictException(
                "المكتب غير مفعّل حالياً ولا يمكن إضافة أعضاء إليه.");
        }

        var member = await _agencies.GetUserAccountAsync(request.MemberUserId, cancellationToken);

        if (member is null)
            throw new NotFoundException("User account was not found.");

        if (member.AgencyId == agency.Id)
        {
            throw new ConflictException(
                "هذا المستخدم عضو في مكتبك بالفعل.");
        }

        if (member.AgencyId is not null)
        {
            throw new ConflictException(
                "هذا المستخدم عضو في مكتب عقاري آخر. عليه مغادرته أولاً.");
        }

        var memberCount = await _agencies.CountMembersAsync(agency.Id, cancellationToken);

        if (memberCount >= Agency.MaxMembers)
        {
            throw new ConflictException(
                $"بلغ المكتب الحد الأقصى للأعضاء ({Agency.MaxMembers}).");
        }

        var now = DateTime.UtcNow;

        member.JoinAgency(agency.Id, now);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Same ordering rationale as CreateAgencyCommandHandler: the Identity store cannot
        // join the save above, so the role grant follows it. A failure here leaves the user
        // attached but without the role — visible on the agency page, able to do nothing
        // extra — which is the safe direction for a partial failure.
        var roleResult = await _identity.AssignRoleAsync(
            userId: member.Id,
            role: RoleNames.AgencyAgent,
            performedByUserId: request.RequestingUserId,
            ipAddress: request.IpAddress,
            ct: cancellationToken);

        if (!roleResult.Succeeded)
        {
            _logger.LogError(
                "User {UserId} joined agency {AgencyId} but the AgencyAgent role could not be granted: {Message}",
                member.Id,
                agency.Id,
                roleResult.Message);
        }

        _logger.LogInformation(
            "Agency member added. AgencyId={AgencyId}, MemberId={MemberId}, AddedBy={AddedBy}",
            agency.Id,
            member.Id,
            request.RequestingUserId);

        return AgencyMapper.ToMemberDto(member, agency.OwnerUserId);
    }
}
