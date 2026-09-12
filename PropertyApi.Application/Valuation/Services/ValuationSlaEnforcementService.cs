using Microsoft.Extensions.Logging;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Valuation.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.Services;

/// <summary>
/// Stage 5 — see <see cref="IValuationSlaEnforcementService"/>. Each phase (inquiries, then
/// invitations) is a two-pass batch: mutate + save all domain transitions first, THEN attempt
/// notifications against the already-committed rows — the same "the change is already
/// committed, so a notification failure must not turn into a lost/rolled-back transition"
/// split CreateAgencyInvitationCommandHandler already applies to its own notification call,
/// just batched here instead of per-request. One item's failure (domain guard or
/// notification) never blocks the rest of the batch, mirroring
/// AccountDeletionSweepHostedService's per-item try/catch.
/// </summary>
public sealed class ValuationSlaEnforcementService : IValuationSlaEnforcementService
{
    private readonly IValuationInquiryRepository _inquiries;
    private readonly IValuationOfficeInvitationRepository _invitations;
    private readonly IAgencyRepository _agencies;
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ValuationSlaEnforcementService> _logger;

    public ValuationSlaEnforcementService(
        IValuationInquiryRepository inquiries,
        IValuationOfficeInvitationRepository invitations,
        IAgencyRepository agencies,
        INotificationService notifications,
        IUnitOfWork unitOfWork,
        ILogger<ValuationSlaEnforcementService> logger)
    {
        _inquiries = inquiries;
        _invitations = invitations;
        _agencies = agencies;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ValuationSlaSweepResult> RunSweepAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken ct = default)
    {
        // Order matters: an inquiry that expires in this same pass immediately makes its own
        // Sent invitations eligible for the second phase below (their parent's ExpiresAt has
        // now definitely passed), so inquiries first means both phases always see a consistent
        // picture within one sweep instead of waiting for the next tick to catch up.
        var inquiriesExpired = await ExpireDueInquiriesAsync(utcNow, batchSize, ct);
        var invitationsExpired = await ExpireStaleInvitationsAsync(utcNow, batchSize, ct);

        return new ValuationSlaSweepResult
        {
            InquiriesExpired = inquiriesExpired,
            InvitationsExpired = invitationsExpired,
        };
    }

    private async Task<int> ExpireDueInquiriesAsync(DateTime utcNow, int batchSize, CancellationToken ct)
    {
        var due = await _inquiries.GetDueForExpiryAsync(utcNow, batchSize, ct);
        if (due.Count == 0)
            return 0;

        // Captured before Expire() overwrites Status — this is the Fast-Path-vs-Office-Path
        // signal NotifyValuationInquiryExpiredAsync needs, read from the inquiry's own existing
        // Status rather than any new field: MatchedFromListings means the Fast Path already
        // produced a preliminary estimate before time ran out; Pending or
        // AwaitingOfficeResponses means it never got that far.
        var expired = new List<(ValuationInquiry Inquiry, bool HadPreliminaryEstimate)>();

        foreach (var inquiry in due)
        {
            try
            {
                var hadPreliminaryEstimate = inquiry.Status == ValuationInquiryStatus.MatchedFromListings;
                inquiry.Expire(utcNow);
                _inquiries.Update(inquiry);
                expired.Add((inquiry, hadPreliminaryEstimate));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire valuation inquiry {InquiryId}.", inquiry.Id);
            }
        }

        if (expired.Count > 0)
            await _unitOfWork.SaveChangesAsync(ct);

        foreach (var (inquiry, hadPreliminaryEstimate) in expired)
        {
            // Anonymous inquiry (guest, no account) — nothing to notify, per
            // ValuationInquiry.RequesterId's own "guest case" nullability.
            if (inquiry.RequesterId is not { } requesterId)
                continue;

            try
            {
                await _notifications.NotifyValuationInquiryExpiredAsync(
                    requesterId, inquiry.Id, hadPreliminaryEstimate, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send valuation-inquiry-expired notification. InquiryId={InquiryId}",
                    inquiry.Id);
            }
        }

        return expired.Count;
    }

    private async Task<int> ExpireStaleInvitationsAsync(DateTime utcNow, int batchSize, CancellationToken ct)
    {
        var stale = await _invitations.GetStaleSentInvitationsAsync(utcNow, batchSize, ct);
        if (stale.Count == 0)
            return 0;

        var expired = new List<ValuationOfficeInvitation>();

        foreach (var invitation in stale)
        {
            try
            {
                invitation.Expire(utcNow);
                _invitations.Update(invitation);
                expired.Add(invitation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire valuation office invitation {InvitationId}.", invitation.Id);
            }
        }

        if (expired.Count > 0)
            await _unitOfWork.SaveChangesAsync(ct);

        foreach (var invitation in expired)
        {
            try
            {
                // No notion of notifying an Agency directly anywhere in this codebase — every
                // agency-facing notification resolves to Agency.OwnerUserId first, same as
                // AcceptAgencyInvitationCommandHandler/DeclineAgencyInvitationCommandHandler
                // already do for their own agency-owner notifications.
                var agency = await _agencies.GetByIdAsync(invitation.AgencyId, ct);
                if (agency is null)
                {
                    _logger.LogWarning(
                        "Agency {AgencyId} not found while notifying about expired valuation invitation {InvitationId}.",
                        invitation.AgencyId,
                        invitation.Id);
                    continue;
                }

                await _notifications.NotifyValuationOfficeInvitationExpiredAsync(
                    agency.OwnerUserId, invitation.Id, invitation.InquiryId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send valuation-office-invitation-expired notification. InvitationId={InvitationId}",
                    invitation.Id);
            }
        }

        return expired.Count;
    }
}
