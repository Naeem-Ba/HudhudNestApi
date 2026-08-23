using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Application.Agencies.Commands.RemoveAgencyMember;

/// <summary>
/// Removes a member from an agency — whether the owner removed them or they left.
///
/// What this does NOT do is touch the member's listings. They belong to the member's
/// OwnerId, stay published, and simply lose the agency badge. An organisation losing a
/// member must never be able to unpublish that person's property listings.
/// </summary>
public sealed class RemoveAgencyMemberCommandHandler
    : IRequestHandler<RemoveAgencyMemberCommand, Unit>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAdminIdentityService _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RemoveAgencyMemberCommandHandler> _logger;

    public RemoveAgencyMemberCommandHandler(
        IAgencyRepository agencies,
        IAdminIdentityService identity,
        IUnitOfWork unitOfWork,
        ILogger<RemoveAgencyMemberCommandHandler> logger)
    {
        _agencies = agencies;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Unit> Handle(
        RemoveAgencyMemberCommand request,
        CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            throw new NotFoundException("Agency was not found.");

        var isOwnerActing = agency.OwnerUserId == request.RequestingUserId;
        var isSelfRemoval = request.MemberUserId == request.RequestingUserId;

        if (!isOwnerActing && !isSelfRemoval)
        {
            throw new ForbiddenException(
                "Only the agency owner can remove other members.");
        }

        // The owner cannot be removed by anyone, including themselves. An agency with no
        // owner has nobody who can administer it, and deleting the agency is a different,
        // more consequential action than leaving it — it must not happen as a side effect
        // of a "remove member" call.
        if (request.MemberUserId == agency.OwnerUserId)
        {
            throw new ConflictException(
                "لا يمكن إزالة مالك المكتب. انقل الملكية أولاً أو احذف المكتب.");
        }

        var member = await _agencies.GetUserAccountAsync(request.MemberUserId, cancellationToken);

        if (member is null)
            throw new NotFoundException("User account was not found.");

        if (member.AgencyId != agency.Id)
        {
            throw new ConflictException(
                "هذا المستخدم ليس عضواً في هذا المكتب.");
        }

        member.LeaveAgency(DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var roleResult = await _identity.RemoveRoleAsync(
            userId: member.Id,
            role: RoleNames.AgencyAgent,
            performedByUserId: request.RequestingUserId,
            ipAddress: request.IpAddress,
            ct: cancellationToken);

        if (!roleResult.Succeeded)
        {
            // Left deliberately as an error log rather than a throw: the membership is
            // already gone, so failing the request would report "removal failed" for a
            // removal that happened. A stale AgencyAgent role grants nothing on its own —
            // every agency handler checks AgencyId, which is now null.
            _logger.LogError(
                "User {UserId} left agency {AgencyId} but the AgencyAgent role could not be removed: {Message}",
                member.Id,
                agency.Id,
                roleResult.Message);
        }

        _logger.LogInformation(
            "Agency member removed. AgencyId={AgencyId}, MemberId={MemberId}, RemovedBy={RemovedBy}, SelfRemoval={SelfRemoval}",
            agency.Id,
            member.Id,
            request.RequestingUserId,
            isSelfRemoval);

        return Unit.Value;
    }
}
