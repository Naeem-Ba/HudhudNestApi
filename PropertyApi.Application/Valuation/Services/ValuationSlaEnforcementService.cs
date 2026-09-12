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
    /// <summary>
    /// Remediation M2 — caps how many <c>batchSize</c>-sized batches EACH phase (inquiries,
    /// then invitations) will drain within a SINGLE RunSweepAsync call. Before this, one call
    /// ever fetched and processed exactly one batch, so a large backlog only drained at
    /// ValuationInquiryExpiryHostedService's own SweepInterval (15 minutes) per batch — with
    /// MaxItemsPerPhase=200, 10,000 due rows would need ~50 sweep ticks (~12.5 hours) to fully
    /// process. Looping here instead — fetch, mutate+save, notify, then fetch again — lets one
    /// tick drain up to MaxBatchesPerPhasePerTick x batchSize rows (with the default
    /// MaxItemsPerPhase=200, that is up to 5,000 rows/phase/tick: a 10,000-row backlog now
    /// drains in ~2 ticks, ~30 minutes, instead of ~12.5 hours), while every individual batch
    /// still gets its own bounded transaction/notification burst exactly as before — this
    /// changes how MANY batches run per tick, not what a batch itself does or how big one is.
    ///
    /// Bounded, not unlimited: an unbounded "keep looping until nothing is left" could turn one
    /// sweep tick into an hours-long, uncancellable run against a truly pathological backlog,
    /// starving graceful shutdown and the PeriodicTimer alike. This cap is a reasoned safety
    /// bound given each iteration's own cost (one fetch query, one save-with-concurrency-retry,
    /// up to batchSize notification calls) — it is not a measured throughput number; no live
    /// database was available to benchmark actual per-batch duration (see this remediation's
    /// own report). If real measurement later shows this is too conservative or too aggressive
    /// for the deployed database's actual capacity, this is the one constant to revisit.
    /// </summary>
    private const int MaxBatchesPerPhasePerTick = 25;

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

        // Remediation M4 — after expiry, so an inquiry that crossed BOTH its 18h and 24h marks
        // in the same tick is expired, not reminded (ApplyDueForReminderFilter's own
        // Status/ExpiresAt guard already excludes anything the expiry phase above just moved
        // out of a non-terminal status, but running reminders second makes that ordering
        // explicit rather than incidental).
        var remindersSent = await SendDueRemindersAsync(utcNow, batchSize, ct);

        // Remediation M3 — retries notifications whose FIRST attempt (here or in
        // SubmitOfficeResponseCommandHandler) failed. Runs every tick, not only when this
        // tick's own phases above found something, so a transient failure from a PREVIOUS tick
        // is not stuck waiting for fresh work to happen to trigger a retry.
        var notificationsRetried = await RetryFailedNotificationsAsync(batchSize, ct);

        return new ValuationSlaSweepResult
        {
            InquiriesExpired = inquiriesExpired,
            InvitationsExpired = invitationsExpired,
            RemindersSent = remindersSent,
            NotificationsRetried = notificationsRetried,
        };
    }

    private async Task<int> ExpireDueInquiriesAsync(DateTime utcNow, int batchSize, CancellationToken ct)
    {
        // Remediation M2 — drain repeated batches within this one call rather than only ever
        // processing one. GetDueForExpiryAsync returning fewer than a full batch is this
        // phase's own signal the backlog is exhausted (ORDER BY ExpiresAt + Take(batchSize):
        // any row still due after this fetch would have to sort before or alongside rows this
        // fetch already returned, which is impossible once the returned count is under the
        // cap) — the same reasoning a keyset/offset-free pagination loop uses to know it has
        // reached the last page.
        var totalExpired = 0;
        for (var iteration = 0; iteration < MaxBatchesPerPhasePerTick; iteration++)
        {
            var due = await _inquiries.GetDueForExpiryAsync(utcNow, batchSize, ct);
            if (due.Count == 0)
                break;

            totalExpired += await ExpireDueInquiriesBatchAsync(due, utcNow, ct);

            if (due.Count < batchSize)
                break;

            ct.ThrowIfCancellationRequested();
        }

        return totalExpired;
    }

    private async Task<int> ExpireDueInquiriesBatchAsync(
        IReadOnlyList<ValuationInquiry> due, DateTime utcNow, CancellationToken ct)
    {
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
        {
            // Remediation H1 — a concurrent SubmitOfficeResponseCommandHandler request may have
            // just completed one of these SAME inquiries (its own last-outstanding-invitation
            // check winning the xmin race a moment before this sweep's own SaveChangesAsync).
            // That row's expiry loses, correctly, but the OTHER up-to-MaxItemsPerPhase inquiries
            // in this same batch must still expire — one lost race must not roll back the rest.
            var dropped = await _unitOfWork.SaveChangesDroppingConcurrencyConflictsAsync(ct);
            if (dropped.Count > 0)
            {
                var droppedSet = new HashSet<object>(dropped, ReferenceEqualityComparer.Instance);
                expired.RemoveAll(x => droppedSet.Contains(x.Inquiry));

                _logger.LogInformation(
                    "{Count} valuation inquiry expiry(ies) lost a concurrency race to a concurrent " +
                    "writer (already resolved elsewhere, e.g. completed) and were skipped this sweep.",
                    dropped.Count);
            }
        }

        var notified = new List<ValuationInquiry>();
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

                // Remediation M3 — stamped only on confirmed success, so
                // GetExpiredAwaitingNotificationAsync's retry query picks up exactly the ones
                // that fail below and none of the ones that just succeeded.
                inquiry.MarkExpiryNotified(utcNow);
                _inquiries.Update(inquiry);
                notified.Add(inquiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send valuation-inquiry-expired notification; will retry next sweep tick. InquiryId={InquiryId}",
                    inquiry.Id);
            }
        }

        if (notified.Count > 0)
        {
            // Best-effort: even if this particular stamp-save loses a race, the notification
            // itself already succeeded above — a rare resulting retry would at worst be a
            // duplicate notification, not a lost one, and no other writer in this codebase
            // mutates an already-Expired ValuationInquiry, so this path is not expected to
            // actually conflict in practice.
            await _unitOfWork.SaveChangesDroppingConcurrencyConflictsAsync(ct);
        }

        return expired.Count;
    }

    private async Task<int> ExpireStaleInvitationsAsync(DateTime utcNow, int batchSize, CancellationToken ct)
    {
        // Remediation M2 — same batch-draining loop as ExpireDueInquiriesAsync above, for the
        // exact same reason: GetStaleSentInvitationsAsync's own ORDER BY + Take(batchSize)
        // means a short return is this phase's own signal the backlog is exhausted.
        var totalExpired = 0;
        for (var iteration = 0; iteration < MaxBatchesPerPhasePerTick; iteration++)
        {
            var stale = await _invitations.GetStaleSentInvitationsAsync(utcNow, batchSize, ct);
            if (stale.Count == 0)
                break;

            totalExpired += await ExpireStaleInvitationsBatchAsync(stale, utcNow, ct);

            if (stale.Count < batchSize)
                break;

            ct.ThrowIfCancellationRequested();
        }

        return totalExpired;
    }

    private async Task<int> ExpireStaleInvitationsBatchAsync(
        IReadOnlyList<ValuationOfficeInvitation> stale, DateTime utcNow, CancellationToken ct)
    {
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
        {
            // Remediation H1 — same reasoning as ExpireDueInquiriesAsync above: a concurrent
            // SubmitOfficeResponseCommandHandler request may have just answered this exact
            // invitation (winning the xmin race a moment before this sweep's SaveChangesAsync).
            // That one row's expiry loses, correctly, but the rest of this batch must still
            // commit.
            var dropped = await _unitOfWork.SaveChangesDroppingConcurrencyConflictsAsync(ct);
            if (dropped.Count > 0)
            {
                var droppedSet = new HashSet<object>(dropped, ReferenceEqualityComparer.Instance);
                expired.RemoveAll(droppedSet.Contains);

                _logger.LogInformation(
                    "{Count} valuation office invitation expiry(ies) lost a concurrency race to a " +
                    "concurrent writer (already responded to) and were skipped this sweep.",
                    dropped.Count);
            }
        }

        var notified = new List<ValuationOfficeInvitation>();
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

                    // Stamped anyway (Remediation M3) — an agency that does not exist will
                    // never start existing again; leaving ExpiryNotifiedAt null would have the
                    // retry phase re-attempt this exact same no-op lookup forever.
                    invitation.MarkExpiryNotified(utcNow);
                    _invitations.Update(invitation);
                    notified.Add(invitation);
                    continue;
                }

                await _notifications.NotifyValuationOfficeInvitationExpiredAsync(
                    agency.OwnerUserId, invitation.Id, invitation.InquiryId, ct);

                // Remediation M3 — stamped only on confirmed success, so
                // GetExpiredAwaitingNotificationAsync's retry query picks up exactly the ones
                // that fail below and none of the ones that just succeeded.
                invitation.MarkExpiryNotified(utcNow);
                _invitations.Update(invitation);
                notified.Add(invitation);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send valuation-office-invitation-expired notification; will retry next sweep tick. InvitationId={InvitationId}",
                    invitation.Id);
            }
        }

        if (notified.Count > 0)
        {
            // Best-effort, same reasoning as ExpireDueInquiriesBatchAsync's own stamp-save.
            await _unitOfWork.SaveChangesDroppingConcurrencyConflictsAsync(ct);
        }

        return expired.Count;
    }

    /// <summary>
    /// Remediation M4 — CreatedAt+18h "closing soon" reminder, at most once per inquiry. Only
    /// a single batch per tick (no MaxBatchesPerPhasePerTick loop): unlike the expiry phases,
    /// there is no 24h-SLA urgency driving this one, and a reminder that lags by one extra
    /// 15-minute tick under an extreme backlog is a minor, non-breaking degradation — not the
    /// same class of problem as a customer never being told their inquiry expired at all.
    /// </summary>
    private async Task<int> SendDueRemindersAsync(DateTime utcNow, int batchSize, CancellationToken ct)
    {
        var due = await _inquiries.GetDueForReminderAsync(utcNow, batchSize, ct);
        if (due.Count == 0)
            return 0;

        // Notify-then-stamp (not stamp-then-notify): ReminderSentAt only means anything
        // together with a successfully delivered notification, same ordering
        // Property.MarkExpiryWarningSent()/SubmitOfficeResponseCommandHandler's own
        // MarkResultReadyNotified already use for this exact kind of "the stamp IS the success
        // record" field — unlike Expire()/MarkCompleted, which are state transitions worth
        // persisting even if their own notification later fails.
        var sent = new List<ValuationInquiry>();
        foreach (var inquiry in due)
        {
            try
            {
                // Guaranteed non-null by ApplyDueForReminderFilter's own RequesterId != null
                // clause — this repository method never returns an anonymous inquiry.
                await _notifications.NotifyValuationInquiryReminderSoonAsync(inquiry.RequesterId!.Value, inquiry.Id, ct);
                inquiry.MarkReminderSent(utcNow);
                _inquiries.Update(inquiry);
                sent.Add(inquiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send valuation-inquiry-reminder-soon notification; will retry next sweep tick. InquiryId={InquiryId}",
                    inquiry.Id);
            }
        }

        if (sent.Count == 0)
            return 0;

        var dropped = await _unitOfWork.SaveChangesDroppingConcurrencyConflictsAsync(ct);
        if (dropped.Count > 0)
        {
            // The only writer that could concurrently touch this same ValuationInquiry row is
            // SubmitOfficeResponseCommandHandler's MarkCompleted or this sweep's own Expire —
            // both change Status away from the non-terminal set ApplyDueForReminderFilter
            // requires. So a dropped stamp here can never cause a duplicate reminder on the
            // next tick: that tick's own query will no longer match this inquiry at all,
            // regardless of ReminderSentAt still reading null.
            var droppedSet = new HashSet<object>(dropped, ReferenceEqualityComparer.Instance);
            sent.RemoveAll(droppedSet.Contains);

            _logger.LogInformation(
                "{Count} valuation inquiry reminder stamp(s) lost a concurrency race (the inquiry " +
                "was concurrently completed/expired) — the reminder was already sent and will not repeat.",
                dropped.Count);
        }

        return sent.Count;
    }

    /// <summary>
    /// Remediation M3 — retries notification types whose first attempt (in this class or in
    /// SubmitOfficeResponseCommandHandler) failed, using each entity's own idempotency stamp
    /// as the "still pending" signal. Runs unconditionally every tick.
    /// </summary>
    private async Task<int> RetryFailedNotificationsAsync(int batchSize, CancellationToken ct)
    {
        var retried = 0;
        retried += await RetryExpiredInquiryNotificationsAsync(batchSize, ct);
        retried += await RetryExpiredInvitationNotificationsAsync(batchSize, ct);
        retried += await RetryResultReadyNotificationsAsync(batchSize, ct);
        return retried;
    }

    private async Task<int> RetryExpiredInquiryNotificationsAsync(int batchSize, CancellationToken ct)
    {
        var pending = await _inquiries.GetExpiredAwaitingNotificationAsync(batchSize, ct);
        if (pending.Count == 0)
            return 0;

        var notified = new List<ValuationInquiry>();
        foreach (var inquiry in pending)
        {
            try
            {
                // The pre-expiry Status (MatchedFromListings vs Pending/AwaitingOfficeResponses)
                // that decided the ORIGINAL attempt's HadPreliminaryEstimate wording is gone by
                // the time this retry path runs — Expire() already overwrote Status to Expired.
                // Deliberate, minor simplification: a retry always uses the generic ("no
                // estimate reached") wording rather than persisting a second field solely to
                // preserve message wording for what is already the rare failed-first-attempt
                // case. This never affects correctness (the notification is still sent exactly
                // once, to the right person, about the right inquiry) — only, in that rare
                // case, its exact phrasing.
                await _notifications.NotifyValuationInquiryExpiredAsync(
                    inquiry.RequesterId!.Value, inquiry.Id, hadPreliminaryEstimate: false, ct);
                inquiry.MarkExpiryNotified(DateTime.UtcNow);
                _inquiries.Update(inquiry);
                notified.Add(inquiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Retry of valuation-inquiry-expired notification failed again; will retry next sweep tick. InquiryId={InquiryId}",
                    inquiry.Id);
            }
        }

        if (notified.Count == 0)
            return 0;

        var dropped = await _unitOfWork.SaveChangesDroppingConcurrencyConflictsAsync(ct);
        if (dropped.Count > 0)
        {
            var droppedSet = new HashSet<object>(dropped, ReferenceEqualityComparer.Instance);
            notified.RemoveAll(droppedSet.Contains);
        }

        return notified.Count;
    }

    private async Task<int> RetryExpiredInvitationNotificationsAsync(int batchSize, CancellationToken ct)
    {
        var pending = await _invitations.GetExpiredAwaitingNotificationAsync(batchSize, ct);
        if (pending.Count == 0)
            return 0;

        var notified = new List<ValuationOfficeInvitation>();
        foreach (var invitation in pending)
        {
            try
            {
                var agency = await _agencies.GetByIdAsync(invitation.AgencyId, ct);
                if (agency is null)
                {
                    // Same defensive path ExpireStaleInvitationsAsync already applies — cannot
                    // notify an owner that does not exist. Stamped anyway so this row is not
                    // retried forever for a condition that will never resolve itself.
                    invitation.MarkExpiryNotified(DateTime.UtcNow);
                    _invitations.Update(invitation);
                    notified.Add(invitation);
                    continue;
                }

                await _notifications.NotifyValuationOfficeInvitationExpiredAsync(
                    agency.OwnerUserId, invitation.Id, invitation.InquiryId, ct);
                invitation.MarkExpiryNotified(DateTime.UtcNow);
                _invitations.Update(invitation);
                notified.Add(invitation);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Retry of valuation-office-invitation-expired notification failed again; will retry next sweep tick. InvitationId={InvitationId}",
                    invitation.Id);
            }
        }

        if (notified.Count == 0)
            return 0;

        var dropped = await _unitOfWork.SaveChangesDroppingConcurrencyConflictsAsync(ct);
        if (dropped.Count > 0)
        {
            var droppedSet = new HashSet<object>(dropped, ReferenceEqualityComparer.Instance);
            notified.RemoveAll(droppedSet.Contains);
        }

        return notified.Count;
    }

    private async Task<int> RetryResultReadyNotificationsAsync(int batchSize, CancellationToken ct)
    {
        var pending = await _inquiries.GetCompletedAwaitingResultNotificationAsync(batchSize, ct);
        if (pending.Count == 0)
            return 0;

        var notified = new List<ValuationInquiry>();
        foreach (var inquiry in pending)
        {
            try
            {
                await _notifications.NotifyValuationResultReadyAsync(inquiry.RequesterId!.Value, inquiry.Id, ct);
                inquiry.MarkResultReadyNotified(DateTime.UtcNow);
                _inquiries.Update(inquiry);
                notified.Add(inquiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Retry of valuation-result-ready notification failed again; will retry next sweep tick. InquiryId={InquiryId}",
                    inquiry.Id);
            }
        }

        if (notified.Count == 0)
            return 0;

        var dropped = await _unitOfWork.SaveChangesDroppingConcurrencyConflictsAsync(ct);
        if (dropped.Count > 0)
        {
            var droppedSet = new HashSet<object>(dropped, ReferenceEqualityComparer.Instance);
            notified.RemoveAll(droppedSet.Contains);
        }

        return notified.Count;
    }
}
