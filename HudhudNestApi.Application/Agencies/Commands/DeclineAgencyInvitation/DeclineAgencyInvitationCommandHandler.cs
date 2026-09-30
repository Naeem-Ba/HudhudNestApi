using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Application.Agencies.Commands.DeclineAgencyInvitation;

/// <summary>
/// Declines an AgencyInvitation. Uses the same AgencyInvitationLock.ForTargetUser key as
/// AcceptAgencyInvitationCommandHandler, so a decline racing an accept for the same
/// invitation (Scenario C) is serialized the same way: whichever request's post-lock
/// reload sees a terminal status loses cleanly.
/// </summary>
public sealed class DeclineAgencyInvitationCommandHandler
    : IRequestHandler<DeclineAgencyInvitationCommand, Unit>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyInvitationRepository _invitations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notifications;
    private readonly ILogger<DeclineAgencyInvitationCommandHandler> _logger;

    public DeclineAgencyInvitationCommandHandler(
        IAgencyRepository agencies,
        IAgencyInvitationRepository invitations,
        IUnitOfWork unitOfWork,
        INotificationService notifications,
        ILogger<DeclineAgencyInvitationCommandHandler> logger)
    {
        _agencies = agencies;
        _invitations = invitations;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Unit> Handle(
        DeclineAgencyInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var invitation = await _invitations.GetByIdAsync(request.InvitationId, cancellationToken);

        if (invitation is null)
            throw new NotFoundException("Invitation was not found.");

        if (invitation.TargetUserId != request.RequestingUserId)
            throw new ForbiddenException("This invitation was not sent to you.");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            try
            {
                await _unitOfWork.AcquireAdvisoryLockAsync(
                    AgencyInvitationLock.ForTargetUser(request.RequestingUserId),
                    cancellationToken);

                await _invitations.ReloadAsync(invitation, cancellationToken);

                invitation.Decline(DateTime.UtcNow);

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
            throw new ConflictException(ex.Message);
        }

        var agency = await _agencies.GetByIdAsync(invitation.AgencyId, cancellationToken);
        var target = await _agencies.GetUserAccountAsync(invitation.TargetUserId, cancellationToken);

        if (agency is not null && target is not null)
        {
            try
            {
                var targetName = string.IsNullOrWhiteSpace(target.DisplayName)
                    ? $"{target.FirstName} {target.LastName}".Trim()
                    : target.DisplayName!;

                await _notifications.NotifyAgencyInvitationRespondedAsync(
                    recipientId: agency.OwnerUserId,
                    invitationId: invitation.Id,
                    targetUserName: targetName,
                    accepted: false,
                    ct: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send agency-invitation-declined notification. InvitationId={InvitationId}, OwnerId={OwnerId}",
                    invitation.Id,
                    agency.OwnerUserId);
            }
        }

        _logger.LogInformation(
            "Agency invitation declined. InvitationId={InvitationId}, AgencyId={AgencyId}, TargetUserId={TargetUserId}",
            invitation.Id,
            invitation.AgencyId,
            invitation.TargetUserId);

        return Unit.Value;
    }
}
