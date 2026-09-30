using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Mapping;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Agencies.Entities;

namespace HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitation;

/// <summary>
/// Creates a Pending AgencyInvitation and notifies the target user. Never attaches anyone
/// to the agency — see AgencyInvitation's doc comment and B-2 (RELEASE-BLOCKERS-AR.md).
/// Membership is only ever created by AcceptAgencyInvitationCommandHandler.
/// </summary>
public sealed class CreateAgencyInvitationCommandHandler
    : IRequestHandler<CreateAgencyInvitationCommand, AgencyInvitationDto>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notifications;
    private readonly ILogger<CreateAgencyInvitationCommandHandler> _logger;

    public CreateAgencyInvitationCommandHandler(
        IAgencyRepository agencies,
        IAgencyInvitationRepository invitations,
        IUnitOfWork unitOfWork,
        INotificationService notifications,
        ILogger<CreateAgencyInvitationCommandHandler> logger)
    {
        _agencies = agencies;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<AgencyInvitationDto> Handle(
        CreateAgencyInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            throw new NotFoundException("Agency was not found.");

        // Holding AgencyOwner is not enough — it must be THIS agency's owner. Same
        // reasoning as AddAgencyMemberCommandHandler used to apply here.
        if (agency.OwnerUserId != request.RequestingUserId)
            throw new ForbiddenException("Only the agency owner can invite members.");

        if (!agency.IsActive)
        {
            throw new ConflictException(
                "المكتب غير مفعّل حالياً ولا يمكن دعوة أعضاء إليه.");
        }

        if (request.TargetUserId == request.RequestingUserId)
        {
            throw new ConflictException("لا يمكنك دعوة نفسك للانضمام إلى مكتبك.");
        }

        var target = await _agencies.GetUserAccountAsync(request.TargetUserId, cancellationToken);

        if (target is null)
            throw new NotFoundException("User account was not found.");

        if (target.AgencyId == agency.Id)
        {
            throw new ConflictException("هذا المستخدم عضو في مكتبك بالفعل.");
        }

        if (target.AgencyId is not null)
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

        var inviter = await _agencies.GetUserAccountAsync(request.RequestingUserId, cancellationToken)
            ?? throw new NotFoundException("User account was not found.");

        AgencyInvitation invitation;
        var now = DateTime.UtcNow;

        // Scenario A (concurrency): two invites for the same agency+user racing each other.
        // The advisory lock serializes them; the filtered unique index on
        // (AgencyId, TargetUserId) WHERE Pending is the backstop if anything ever calls
        // AddAsync outside this lock.
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await _unitOfWork.AcquireAdvisoryLockAsync(
                AgencyInvitationLock.ForPair(agency.Id, target.Id),
                cancellationToken);

            var existing = await _invitations.GetPendingAsync(agency.Id, target.Id, cancellationToken);

            if (existing is not null)
            {
                // A stale Pending row whose window already passed is not a live duplicate —
                // flip it to Expired and let a fresh invitation through, rather than making
                // the owner wait 14 days for a job that does not exist to clean it up.
                if (!existing.MarkExpiredIfDue(now))
                {
                    throw new ConflictException(
                        "توجد دعوة معلّقة بالفعل لهذا المستخدم في هذا المكتب.");
                }
            }

            invitation = AgencyInvitation.Create(
                agencyId: agency.Id,
                inviterUserId: request.RequestingUserId,
                targetUserId: target.Id,
                utcNow: now);

            await _invitations.AddAsync(invitation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        // Best-effort, same pattern as RequestVisitCommandHandler: the invitation is already
        // committed above, so a notification failure must not turn into a 500 for a request
        // that actually succeeded.
        try
        {
            var inviterName = string.IsNullOrWhiteSpace(inviter.DisplayName)
                ? $"{inviter.FirstName} {inviter.LastName}".Trim()
                : inviter.DisplayName!;

            await _notifications.NotifyAgencyInvitationReceivedAsync(
                recipientId: target.Id,
                invitationId: invitation.Id,
                agencyName: agency.Name,
                inviterName: inviterName,
                ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send agency-invitation notification. InvitationId={InvitationId}, AgencyId={AgencyId}, TargetUserId={TargetUserId}",
                invitation.Id,
                agency.Id,
                target.Id);
        }

        _logger.LogInformation(
            "Agency invitation created. InvitationId={InvitationId}, AgencyId={AgencyId}, TargetUserId={TargetUserId}, InvitedBy={InvitedBy}",
            invitation.Id,
            agency.Id,
            target.Id,
            request.RequestingUserId);

        return AgencyMapper.ToInvitationDto(invitation, agency, inviter);
    }
}
