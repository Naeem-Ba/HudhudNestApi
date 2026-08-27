using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Agencies.DTOs;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Agencies.Mapping;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Application.Agencies.Commands.AcceptAgencyInvitation;

/// <summary>
/// Accepts an AgencyInvitation and — only here, nowhere else — creates the membership
/// (B-2, RELEASE-BLOCKERS-AR.md).
///
/// Concurrency: everything that can end this invitation for this target user (accepting
/// it, declining it, or accepting a different agency's invitation instead — JoinAgency
/// allows only one) is serialized on AgencyInvitationLock.ForTargetUser. The invitation is
/// loaded once up front for the 404/ownership checks, then reloaded from the database
/// after the lock is acquired — see IAgencyInvitationRepository.ReloadAsync — so a
/// transition committed by a concurrent request while this one waited for the lock is
/// what this request's status check actually sees, not a stale pre-lock copy.
/// </summary>
public sealed class AcceptAgencyInvitationCommandHandler
    : IRequestHandler<AcceptAgencyInvitationCommand, AgencyMemberDto>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyInvitationRepository _invitations;
    private readonly IAdminIdentityService _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notifications;
    private readonly ILogger<AcceptAgencyInvitationCommandHandler> _logger;

    public AcceptAgencyInvitationCommandHandler(
        IAgencyRepository agencies,
        IAgencyInvitationRepository invitations,
        IAdminIdentityService identity,
        IUnitOfWork unitOfWork,
        INotificationService notifications,
        ILogger<AcceptAgencyInvitationCommandHandler> logger)
    {
        _agencies = agencies;
        _invitations = invitations;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<AgencyMemberDto> Handle(
        AcceptAgencyInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var invitation = await _invitations.GetByIdAsync(request.InvitationId, cancellationToken);

        if (invitation is null)
            throw new NotFoundException("Invitation was not found.");

        // Only the invited user may accept their own invitation — never the inviter, never
        // any other caller who happens to know the id.
        if (invitation.TargetUserId != request.RequestingUserId)
            throw new ForbiddenException("This invitation was not sent to you.");

        Agency agency;
        Domain.Users.Entities.UserAccount member;
        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            try
            {
                await _unitOfWork.AcquireAdvisoryLockAsync(
                    AgencyInvitationLock.ForTargetUser(request.RequestingUserId),
                    cancellationToken);

                await _invitations.ReloadAsync(invitation, cancellationToken);

                // Throws DomainException on anything but Pending — including a Pending row
                // whose window already passed, which it also flips to Expired first. That
                // closes "Expired/Accepted/Declined → Accept" and, combined with the lock
                // above, "accept twice" and "accept vs. decline" (Scenarios B/C/D): whichever
                // request's reload sees Accepted/Declined/Expired here loses cleanly instead
                // of creating a second membership.
                invitation.Accept(now);

                member = await _agencies.GetUserAccountAsync(invitation.TargetUserId, cancellationToken)
                    ?? throw new NotFoundException("User account was not found.");

                // Scenario E: the user could have joined a different agency through some
                // other path while this invitation sat Pending. JoinAgency would throw
                // InvalidOperationException for that anyway, but checking here gives a
                // proper ConflictException instead of a 500.
                if (member.AgencyId is not null)
                {
                    throw new ConflictException(
                        "أنت عضو في مكتب عقاري بالفعل. غادره أولاً قبل قبول دعوة أخرى.");
                }

                agency = await _agencies.GetByIdAsync(invitation.AgencyId, cancellationToken)
                    ?? throw new NotFoundException("Agency was not found.");

                if (agency.IsDeleted || !agency.IsActive)
                {
                    throw new ConflictException(
                        "المكتب غير مفعّل حالياً ولا يمكن الانضمام إليه.");
                }

                var memberCount = await _agencies.CountMembersAsync(agency.Id, cancellationToken);

                if (memberCount >= Agency.MaxMembers)
                {
                    throw new ConflictException(
                        $"بلغ المكتب الحد الأقصى للأعضاء ({Agency.MaxMembers}).");
                }

                member.JoinAgency(agency.Id, now);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        }
        catch (DomainException ex)
        {
            // Accept()'s own state-machine guard — translated to the same exception type
            // every other "wrong state" conflict in this handler uses, so the API answers
            // consistently regardless of which check actually caught the race.
            throw new ConflictException(ex.Message);
        }

        // Same ordering rationale as AddAgencyMemberCommandHandler used to carry: Identity
        // cannot enlist in the transaction above, so the role grant follows it.
        var roleResult = await _identity.AssignRoleAsync(
            userId: member.Id,
            role: RoleNames.AgencyAgent,
            performedByUserId: request.RequestingUserId,
            ipAddress: request.IpAddress,
            ct: cancellationToken);

        if (!roleResult.Succeeded)
        {
            _logger.LogError(
                "User {UserId} accepted agency invitation {InvitationId} but the AgencyAgent role could not be granted: {Message}",
                member.Id,
                invitation.Id,
                roleResult.Message);
        }

        try
        {
            var memberName = string.IsNullOrWhiteSpace(member.DisplayName)
                ? $"{member.FirstName} {member.LastName}".Trim()
                : member.DisplayName!;

            await _notifications.NotifyAgencyInvitationRespondedAsync(
                recipientId: agency.OwnerUserId,
                invitationId: invitation.Id,
                targetUserName: memberName,
                accepted: true,
                ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send agency-invitation-accepted notification. InvitationId={InvitationId}, OwnerId={OwnerId}",
                invitation.Id,
                agency.OwnerUserId);
        }

        _logger.LogInformation(
            "Agency invitation accepted. InvitationId={InvitationId}, AgencyId={AgencyId}, MemberId={MemberId}",
            invitation.Id,
            agency.Id,
            member.Id);

        return AgencyMapper.ToMemberDto(member, agency.OwnerUserId);
    }
}
