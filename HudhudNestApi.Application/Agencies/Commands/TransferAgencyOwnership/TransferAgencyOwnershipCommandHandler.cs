using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Mapping;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Application.Agencies.Commands.TransferAgencyOwnership;

public sealed class TransferAgencyOwnershipCommandHandler
    : IRequestHandler<TransferAgencyOwnershipCommand, AgencyDto>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAdminIdentityService _identity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TransferAgencyOwnershipCommandHandler> _logger;

    public TransferAgencyOwnershipCommandHandler(
        IAgencyRepository agencies,
        IAdminIdentityService identity,
        IUnitOfWork unitOfWork,
        ILogger<TransferAgencyOwnershipCommandHandler> logger)
    {
        _agencies = agencies;
        _identity = identity;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AgencyDto> Handle(
        TransferAgencyOwnershipCommand request,
        CancellationToken cancellationToken)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, cancellationToken);

        if (agency is null || agency.IsDeleted)
            throw new NotFoundException("Agency was not found.");

        // Holding AgencyOwner is not enough — it must be THIS agency's owner. Same check
        // every other owner-only Agency handler makes.
        if (agency.OwnerUserId != request.RequestingUserId)
            throw new ForbiddenException("Only the agency owner can transfer ownership.");

        var newOwner = await _agencies.GetUserAccountAsync(
            request.NewOwnerUserId, cancellationToken);

        if (newOwner is null)
            throw new NotFoundException("User account was not found.");

        // Membership rule (see the command's doc comment): the new owner must already be a
        // member of THIS agency. Membership is only ever granted by accepting an
        // AgencyInvitation, so this is the same consent guarantee B-2 built, applied to the
        // more consequential act of handing over ownership.
        if (newOwner.AgencyId != agency.Id)
        {
            throw new ConflictException(
                "يجب أن يكون المالك الجديد عضواً في هذا المكتب قبل نقل الملكية إليه.");
        }

        var now = DateTime.UtcNow;
        var previousOwnerId = agency.OwnerUserId;

        try
        {
            // Guards newOwnerUserId == Guid.Empty and newOwnerUserId == current owner —
            // the latter is already excluded by the membership check above being a no-op
            // (the current owner is trivially a member), so this is a backstop, not the
            // primary check.
            agency.TransferOwnership(request.NewOwnerUserId, now);
        }
        catch (DomainException ex)
        {
            // Same translation AcceptAgencyInvitationCommandHandler applies to its own
            // domain state-machine guard: whichever check catches an invalid transfer, the
            // API answers with the same 409 Conflict.
            throw new ConflictException(ex.Message);
        }

        // GetByIdAsync returns a tracked entity (see AgencyRepository), so this alone
        // persists the mutation above — no separate Update() call needed. Nothing else in
        // the database changes: membership (UserAccount.AgencyId) is untouched by a
        // transfer, only Agency.OwnerUserId moves, so a single SaveChangesAsync is already
        // atomic — no explicit transaction needed (same reasoning as
        // UpdateAgencyCommandHandler/DeactivateAgencyCommandHandler).
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Identity lives in its own store and cannot enlist in the SaveChangesAsync above,
        // so the role swap follows it — same ordering CreateAgencyCommandHandler and
        // AcceptAgencyInvitationCommandHandler use. The Agency row is the source of truth
        // for who owns it (every handler compares against OwnerUserId, not the role), so a
        // role-grant failure here is recoverable by an administrator and logged rather than
        // thrown — failing the request would report "transfer failed" for a transfer that
        // already committed.
        var previousOwnerDemoted = await _identity.RemoveRoleAsync(
            userId: previousOwnerId,
            role: RoleNames.AgencyOwner,
            performedByUserId: request.RequestingUserId,
            ipAddress: request.IpAddress,
            ct: cancellationToken);

        if (!previousOwnerDemoted.Succeeded)
        {
            _logger.LogError(
                "Agency {AgencyId} ownership transferred but the previous owner {PreviousOwnerId} could not lose the AgencyOwner role: {Message}",
                agency.Id,
                previousOwnerId,
                previousOwnerDemoted.Message);
        }

        // The previous owner remains a plain member of the agency (TransferOwnership does
        // not touch UserAccount.AgencyId) — make sure their role reflects that instead of
        // leaving them with no agency role at all.
        var previousOwnerKeptAsMember = await _identity.AssignRoleAsync(
            userId: previousOwnerId,
            role: RoleNames.AgencyAgent,
            performedByUserId: request.RequestingUserId,
            ipAddress: request.IpAddress,
            ct: cancellationToken);

        if (!previousOwnerKeptAsMember.Succeeded)
        {
            _logger.LogError(
                "Agency {AgencyId} ownership transferred but the previous owner {PreviousOwnerId} could not be granted AgencyAgent: {Message}",
                agency.Id,
                previousOwnerId,
                previousOwnerKeptAsMember.Message);
        }

        var newOwnerPromoted = await _identity.AssignRoleAsync(
            userId: request.NewOwnerUserId,
            role: RoleNames.AgencyOwner,
            performedByUserId: request.RequestingUserId,
            ipAddress: request.IpAddress,
            ct: cancellationToken);

        if (!newOwnerPromoted.Succeeded)
        {
            _logger.LogError(
                "Agency {AgencyId} ownership transferred but the new owner {NewOwnerId} could not be granted AgencyOwner: {Message}",
                agency.Id,
                request.NewOwnerUserId,
                newOwnerPromoted.Message);
        }

        _logger.LogInformation(
            "Agency ownership transferred. AgencyId={AgencyId}, PreviousOwnerId={PreviousOwnerId}, NewOwnerId={NewOwnerId}",
            agency.Id,
            previousOwnerId,
            request.NewOwnerUserId);

        var members = await _agencies.GetMembersAsync(agency.Id, cancellationToken);

        return AgencyMapper.ToDto(agency, members, members.Count);
    }
}
