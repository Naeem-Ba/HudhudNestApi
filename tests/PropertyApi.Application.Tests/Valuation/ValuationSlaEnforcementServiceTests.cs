using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Application.Valuation.Services;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Tests.Valuation;

/// <summary>
/// Stage 5 — ValuationSlaEnforcementService's own decision logic (which rows get expired, the
/// Fast-Path-vs-Office-Path notification distinction, idempotency against already-terminal
/// rows, and per-item failure isolation). Uses Moq for every dependency, MockBehavior.Strict,
/// same convention OfficeMatchingServiceTests (Stage 4) already established for this module.
///
/// The real DB-side "due for expiry"/"stale sent invitation" query logic
/// (ValuationInquiryRepository.ApplyDueForExpiryFilter /
/// ValuationOfficeInvitationRepository.ApplyStaleSentFilter) is separately covered, LINQ-to-
/// Objects, in PropertyApi.Architecture.Tests/Persistence/ValuationSlaFilterTests.cs — same
/// split Stage 3/4 already established for their own repository filter logic.
/// </summary>
public sealed class ValuationSlaEnforcementServiceTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunSweepAsync_PendingInquiryPastExpiry_IsExpired_AndRequesterNotified_WithoutPreliminaryEstimateFlag()
    {
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId, ValuationInquiryStatus.Pending);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, [inquiry]);
        SetupStaleInvitations(invitations, []);
        unitOfWork.Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<object>());
        notifications
            .Setup(x => x.NotifyValuationInquiryExpiredAsync(requesterId, inquiry.Id, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(1, result.InquiriesExpired);
        Assert.Equal(0, result.InvitationsExpired);
        Assert.Equal(ValuationInquiryStatus.Expired, inquiry.Status);
        // Twice: once for Expire(), once more for MarkExpiryNotified (Remediation M3) after
        // the notification below succeeds.
        inquiries.Verify(x => x.Update(inquiry), Times.Exactly(2));
        notifications.Verify(
            x => x.NotifyValuationInquiryExpiredAsync(requesterId, inquiry.Id, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_MatchedFromListingsInquiryPastExpiry_NotifiesWithPreliminaryEstimateFlagTrue()
    {
        // The Fast-Path-vs-Office-Path distinction: an inquiry that already had a preliminary
        // estimate (MatchedFromListings) before its 24h window closed must say so, not send the
        // same generic "no estimate at all" message a Pending/AwaitingOfficeResponses inquiry gets.
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId, ValuationInquiryStatus.MatchedFromListings);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, [inquiry]);
        SetupStaleInvitations(invitations, []);
        unitOfWork.Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<object>());
        notifications
            .Setup(x => x.NotifyValuationInquiryExpiredAsync(requesterId, inquiry.Id, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(1, result.InquiriesExpired);
        notifications.Verify(
            x => x.NotifyValuationInquiryExpiredAsync(requesterId, inquiry.Id, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_AnonymousInquiryPastExpiry_IsExpired_ButNeverNotified()
    {
        // requesterId: null — a guest inquiry. Expiring it is still required (never left
        // indefinitely pending just because there is no one to notify), but there is no
        // account to notify, so INotificationService must never be called for it — MockBehavior
        // .Strict on `notifications` below means any unexpected call fails the test outright.
        var inquiry = BuildInquiry(requesterId: null, ValuationInquiryStatus.AwaitingOfficeResponses);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, [inquiry]);
        SetupStaleInvitations(invitations, []);
        unitOfWork.Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<object>());

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(1, result.InquiriesExpired);
        Assert.Equal(ValuationInquiryStatus.Expired, inquiry.Status);
    }

    [Fact]
    public async Task RunSweepAsync_NoInquiriesOrInvitationsDue_SavesNothing_NotifiesNothing()
    {
        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, []);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(0, result.InquiriesExpired);
        Assert.Equal(0, result.InvitationsExpired);
        // Strict mock on unitOfWork with no SaveChangesDroppingConcurrencyConflictsAsync setup
        // means this would already throw if called — the assertion below is redundant
        // confirmation of that.
        unitOfWork.Verify(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunSweepAsync_SentInvitation_WhoseInquiryExpiresAt_HasPassed_IsExpired_AndAgencyOwnerNotified()
    {
        var inquiryId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var invitation = BuildInvitation(agencyId, inquiryId);
        var agency = BuildAgency(agencyId, ownerUserId);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, [invitation]);
        agencies.Setup(x => x.GetByIdAsync(agencyId, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        unitOfWork.Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<object>());
        notifications
            .Setup(x => x.NotifyValuationOfficeInvitationExpiredAsync(ownerUserId, invitation.Id, inquiryId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(0, result.InquiriesExpired);
        Assert.Equal(1, result.InvitationsExpired);
        Assert.Equal(ValuationOfficeInvitationStatus.Expired, invitation.Status);
        notifications.Verify(
            x => x.NotifyValuationOfficeInvitationExpiredAsync(ownerUserId, invitation.Id, inquiryId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_SentInvitation_WhoseAgencyNoLongerExists_IsStillExpired_ButNeverNotified()
    {
        // Defensive path: GetStaleSentInvitationsAsync returned a row, but the agency lookup
        // came back null (should not normally happen given the FK, but the sweep must not
        // crash or skip the domain transition over it). No notification call is set up on the
        // Strict mock, so this also proves the code path never even attempts one.
        var invitation = BuildInvitation(Guid.NewGuid(), Guid.NewGuid());

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, [invitation]);
        agencies.Setup(x => x.GetByIdAsync(invitation.AgencyId, It.IsAny<CancellationToken>())).ReturnsAsync((Agency?)null);
        unitOfWork.Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<object>());

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(1, result.InvitationsExpired);
        Assert.Equal(ValuationOfficeInvitationStatus.Expired, invitation.Status);
    }

    [Fact]
    public async Task RunSweepAsync_OneInquirysNotificationFails_DoesNotStopTheRestOfTheBatch()
    {
        var failingRequesterId = Guid.NewGuid();
        var okRequesterId = Guid.NewGuid();
        var failingInquiry = BuildInquiry(failingRequesterId, ValuationInquiryStatus.Pending);
        var okInquiry = BuildInquiry(okRequesterId, ValuationInquiryStatus.Pending);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, [failingInquiry, okInquiry]);
        SetupStaleInvitations(invitations, []);
        unitOfWork.Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<object>());
        notifications
            .Setup(x => x.NotifyValuationInquiryExpiredAsync(failingRequesterId, failingInquiry.Id, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR hub unreachable"));
        notifications
            .Setup(x => x.NotifyValuationInquiryExpiredAsync(okRequesterId, okInquiry.Id, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        // Both are still expired and counted -- a notification failure never rolls back or
        // skips the already-committed domain transition, and never stops the batch's other
        // items from being processed (same "log and continue" precedent
        // AccountDeletionSweepHostedService applies per item).
        Assert.Equal(2, result.InquiriesExpired);
        Assert.Equal(ValuationInquiryStatus.Expired, failingInquiry.Status);
        Assert.Equal(ValuationInquiryStatus.Expired, okInquiry.Status);
        notifications.Verify(
            x => x.NotifyValuationInquiryExpiredAsync(okRequesterId, okInquiry.Id, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_AlreadyTerminalInquiry_IsNeverReturnedByRepository_SoNeverTouchedOrNotifiedTwice()
    {
        // Idempotency: GetDueForExpiryAsync's own contract (per its doc comment / the
        // repository-level filter tests) is to never return a Completed/Expired row in the
        // first place. This test documents that the service trusts that contract rather than
        // re-checking Status itself -- setting up the mock to return nothing proves no
        // transition/notification happens when the repository (correctly) filters everything
        // out.
        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, []);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(0, result.InquiriesExpired);
    }

    [Fact]
    public async Task RunSweepAsync_OneInquiryLosesConcurrencyRaceDuringSave_OtherInquiryInSameBatchStillExpires_AndTheLoserIsNeverNotified()
    {
        // Remediation H1 — models SubmitOfficeResponseCommandHandler having just completed
        // `racedInquiry` concurrently (winning its xmin race a moment before this sweep's own
        // save). `okInquiry` has no such conflict and must still expire and notify normally —
        // one lost race in a batch must not roll back or skip the rest of that same batch.
        var racedRequesterId = Guid.NewGuid();
        var okRequesterId = Guid.NewGuid();
        var racedInquiry = BuildInquiry(racedRequesterId, ValuationInquiryStatus.Pending);
        var okInquiry = BuildInquiry(okRequesterId, ValuationInquiryStatus.Pending);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, [racedInquiry, okInquiry]);
        SetupStaleInvitations(invitations, []);
        unitOfWork
            .Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object[] { racedInquiry });
        notifications
            .Setup(x => x.NotifyValuationInquiryExpiredAsync(okRequesterId, okInquiry.Id, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        // Only the survivor counts and gets notified. Strict mock on `notifications` has no
        // setup for racedRequesterId -- reaching it would already throw, proving the sweep
        // never notifies for a row it lost the race on (that row was already resolved by
        // whoever won it, which is responsible for its own notification, if any).
        Assert.Equal(1, result.InquiriesExpired);
        notifications.Verify(
            x => x.NotifyValuationInquiryExpiredAsync(okRequesterId, okInquiry.Id, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_InvitationLosesConcurrencyRaceDuringSave_IsNeverNotified_AndDoesNotStopTheBatch()
    {
        // Same reasoning as the inquiry-side test above, for the invitation phase: a
        // concurrent SubmitOfficeResponseCommandHandler request answered this exact invitation
        // just before the sweep's own save.
        var agencyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var invitation = BuildInvitation(agencyId, Guid.NewGuid());
        var agency = BuildAgency(agencyId, ownerUserId);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, [invitation]);
        unitOfWork
            .Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new object[] { invitation });

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(0, result.InvitationsExpired);
        // Strict mocks on `agencies`/`notifications` have no setup -- reaching either would
        // already throw, proving a row that lost the concurrency race is never looked up or
        // notified as if this sweep had actually expired it.
    }

    [Fact]
    public async Task RunSweepAsync_BacklogLargerThanOneBatch_DrainsMultipleBatchesInOneCall()
    {
        // Remediation M2 — before this fix, one RunSweepAsync call ever fetched and processed
        // exactly one batch, so draining a backlog bigger than `batchSize` required waiting for
        // ValuationInquiryExpiryHostedService's own 15-minute timer to tick again per batch.
        // With batchSize=2 and 5 due inquiries arriving as three successive fetches (2, 2, 1),
        // a single RunSweepAsync call must now drain all three batches and report all 5 —
        // proving the fetch/process/persist/next-batch loop actually loops, not merely that it
        // still lets a caller request a smaller batchSize.
        var inquiryA = BuildInquiry(Guid.NewGuid(), ValuationInquiryStatus.Pending);
        var inquiryB = BuildInquiry(Guid.NewGuid(), ValuationInquiryStatus.Pending);
        var inquiryC = BuildInquiry(Guid.NewGuid(), ValuationInquiryStatus.Pending);
        var inquiryD = BuildInquiry(Guid.NewGuid(), ValuationInquiryStatus.Pending);
        var inquiryE = BuildInquiry(Guid.NewGuid(), ValuationInquiryStatus.Pending);
        var allInquiries = new[] { inquiryA, inquiryB, inquiryC, inquiryD, inquiryE };

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        // Three fetches: 2, then 2, then 1 (shorter than batchSize=2, the loop's own signal the
        // backlog is exhausted) — then SetupStaleInvitations always returns empty (unrelated
        // phase, exercised by the other tests above).
        inquiries
            .SetupSequence(x => x.GetDueForExpiryAsync(Now, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { inquiryA, inquiryB })
            .ReturnsAsync(new[] { inquiryC, inquiryD })
            .ReturnsAsync(new[] { inquiryE });
        SetupStaleInvitations(invitations, []);
        unitOfWork
            .Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<object>());
        foreach (var inquiry in allInquiries)
        {
            notifications
                .Setup(x => x.NotifyValuationInquiryExpiredAsync(
                    inquiry.RequesterId!.Value, inquiry.Id, false, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        var result = await service.RunSweepAsync(Now, batchSize: 2);

        Assert.Equal(5, result.InquiriesExpired);
        foreach (var inquiry in allInquiries)
        {
            Assert.Equal(ValuationInquiryStatus.Expired, inquiry.Status);
        }
        inquiries.Verify(
            x => x.GetDueForExpiryAsync(Now, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        // At least 3 (one save per drained batch's Expire() transitions) -- each batch also
        // triggers a second, separate save for the ExpiryNotifiedAt stamp once its
        // notification succeeds (Remediation M3), so the exact total depends on both
        // mechanisms rather than being a single meaningful number to pin here.
        unitOfWork.Verify(
            x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()),
            Times.AtLeast(3));
    }

    [Fact]
    public async Task RunSweepAsync_CalledTenTimesPastThe18HourMark_SendsExactlyOneReminder_NotTen()
    {
        // Remediation M4's own explicit idempotency requirement: ten sweep ticks after the 18h
        // mark must produce ONE reminder, not ten. GetDueForReminderAsync here behaves like the
        // real repository query would (filters live against the entity's OWN current
        // ReminderSentAt, which SendDueRemindersAsync mutates in place on success) rather than
        // returning a fixed canned list every call -- this is what actually proves the
        // idempotency stamp is checked, not merely that a mock happens to be called once.
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId, ValuationInquiryStatus.AwaitingOfficeResponses);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, []);
        inquiries
            .Setup(x => x.GetDueForReminderAsync(Now, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => inquiry.ReminderSentAt is null
                ? new[] { inquiry }
                : Array.Empty<ValuationInquiry>());
        unitOfWork
            .Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<object>());
        notifications
            .Setup(x => x.NotifyValuationInquiryReminderSoonAsync(requesterId, inquiry.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        for (var tick = 0; tick < 10; tick++)
        {
            var result = await service.RunSweepAsync(Now, batchSize: 200);
            Assert.Equal(tick == 0 ? 1 : 0, result.RemindersSent);
        }

        Assert.NotNull(inquiry.ReminderSentAt);
        notifications.Verify(
            x => x.NotifyValuationInquiryReminderSoonAsync(requesterId, inquiry.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_InquiryCompletedBefore18Hours_NeverReceivesAReminder()
    {
        // Test D from the remediation brief: a Completed inquiry must never get a reminder,
        // regardless of how much time has passed since it was created.
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId, ValuationInquiryStatus.MatchedFromListings);
        inquiry.MarkCompleted(Now.AddHours(-20));

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, []);
        // GetDueForReminderAsync's own repository-level filter (ApplyDueForReminderFilter,
        // covered separately in ValuationSlaFilterTests) already excludes Completed rows --
        // this Strict mock simply reflects that contract rather than re-deriving it.

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(0, result.RemindersSent);
        // Strict mock on `notifications` has no reminder setup -- reaching it would already throw.
    }

    // ── Remediation M3 — notification-retry phases ──────────────────

    [Fact]
    public async Task RunSweepAsync_PreviouslyFailedInquiryExpiryNotification_IsRetried_AndSucceeds()
    {
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId, ValuationInquiryStatus.Pending);
        inquiry.Expire(Now.AddMinutes(-30)); // already expired by an earlier, failed sweep tick

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, []);
        inquiries
            .Setup(x => x.GetExpiredAwaitingNotificationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { inquiry });
        unitOfWork
            .Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<object>());
        notifications
            .Setup(x => x.NotifyValuationInquiryExpiredAsync(requesterId, inquiry.Id, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(1, result.NotificationsRetried);
        Assert.NotNull(inquiry.ExpiryNotifiedAt);
        notifications.Verify(
            x => x.NotifyValuationInquiryExpiredAsync(requesterId, inquiry.Id, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunSweepAsync_PreviouslyFailedInvitationExpiryNotification_IsRetried_AndSucceeds()
    {
        var agencyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var invitation = BuildInvitation(agencyId, Guid.NewGuid());
        invitation.Expire(Now.AddMinutes(-30));
        var agency = BuildAgency(agencyId, ownerUserId);

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, []);
        invitations
            .Setup(x => x.GetExpiredAwaitingNotificationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { invitation });
        agencies.Setup(x => x.GetByIdAsync(agencyId, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        unitOfWork
            .Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<object>());
        notifications
            .Setup(x => x.NotifyValuationOfficeInvitationExpiredAsync(ownerUserId, invitation.Id, invitation.InquiryId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(1, result.NotificationsRetried);
        Assert.NotNull(invitation.ExpiryNotifiedAt);
    }

    [Fact]
    public async Task RunSweepAsync_PreviouslyFailedResultReadyNotification_IsRetried_AndSucceeds()
    {
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId, ValuationInquiryStatus.MatchedFromListings);
        inquiry.MarkCompleted(Now.AddMinutes(-30)); // completed by SubmitOfficeResponseCommandHandler, notify failed

        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, []);
        inquiries
            .Setup(x => x.GetCompletedAwaitingResultNotificationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { inquiry });
        unitOfWork
            .Setup(x => x.SaveChangesDroppingConcurrencyConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<object>());
        notifications
            .Setup(x => x.NotifyValuationResultReadyAsync(requesterId, inquiry.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(1, result.NotificationsRetried);
        Assert.NotNull(inquiry.ResultReadyNotifiedAt);
    }

    [Fact]
    public async Task RunSweepAsync_NoFailedNotificationsPending_RetriesNothing()
    {
        var (service, inquiries, invitations, agencies, notifications, unitOfWork) = Build();

        SetupDueInquiries(inquiries, []);
        SetupStaleInvitations(invitations, []);
        // All three GetXAwaitingNotificationAsync calls use Build()'s own empty defaults.

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(0, result.NotificationsRetried);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static (
        ValuationSlaEnforcementService Service,
        Mock<IValuationInquiryRepository> Inquiries,
        Mock<IValuationOfficeInvitationRepository> Invitations,
        Mock<IAgencyRepository> Agencies,
        Mock<INotificationService> Notifications,
        Mock<IUnitOfWork> UnitOfWork) Build()
    {
        var inquiries = new Mock<IValuationInquiryRepository>(MockBehavior.Strict);
        var invitations = new Mock<IValuationOfficeInvitationRepository>(MockBehavior.Strict);
        var agencies = new Mock<IAgencyRepository>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        inquiries.Setup(x => x.Update(It.IsAny<ValuationInquiry>()));
        invitations.Setup(x => x.Update(It.IsAny<ValuationOfficeInvitation>()));

        // Remediation M3/M4 — every RunSweepAsync call now also runs the reminder phase and
        // the three notification-retry phases unconditionally. Defaulted to "nothing pending"
        // here so every EXISTING test above (which predates M3/M4 and is not testing them)
        // does not need its own setup for these — tests that specifically exercise M3/M4
        // override these with their own Setup/SetupSequence.
        inquiries
            .Setup(x => x.GetDueForReminderAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ValuationInquiry>());
        inquiries
            .Setup(x => x.GetExpiredAwaitingNotificationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ValuationInquiry>());
        inquiries
            .Setup(x => x.GetCompletedAwaitingResultNotificationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ValuationInquiry>());
        invitations
            .Setup(x => x.GetExpiredAwaitingNotificationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ValuationOfficeInvitation>());

        var service = new ValuationSlaEnforcementService(
            inquiries.Object,
            invitations.Object,
            agencies.Object,
            notifications.Object,
            unitOfWork.Object,
            NullLogger<ValuationSlaEnforcementService>.Instance);

        return (service, inquiries, invitations, agencies, notifications, unitOfWork);
    }

    private static void SetupDueInquiries(Mock<IValuationInquiryRepository> inquiries, IReadOnlyList<ValuationInquiry> due)
        => inquiries
            .Setup(x => x.GetDueForExpiryAsync(Now, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(due);

    private static void SetupStaleInvitations(Mock<IValuationOfficeInvitationRepository> invitations, IReadOnlyList<ValuationOfficeInvitation> stale)
        => invitations
            .Setup(x => x.GetStaleSentInvitationsAsync(Now, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale);

    private static ValuationInquiry BuildInquiry(Guid? requesterId, ValuationInquiryStatus status)
    {
        // Created far enough in the past that ExpiresAt (Created + 24h) has already passed as
        // of `Now` — the exact condition GetDueForExpiryAsync's contract requires callers of
        // this stub to have already filtered for.
        var createdAt = Now.AddHours(-25);
        var inquiry = ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: createdAt,
            requesterId: requesterId);

        if (status == ValuationInquiryStatus.MatchedFromListings)
        {
            inquiry.MarkMatchedFromListings(createdAt);
        }
        else if (status == ValuationInquiryStatus.AwaitingOfficeResponses)
        {
            inquiry.MarkAwaitingOfficeResponses(createdAt);
        }

        return inquiry;
    }

    private static ValuationOfficeInvitation BuildInvitation(Guid agencyId, Guid inquiryId)
        => ValuationOfficeInvitation.Create(agencyId, inquiryId, ValuationMatchLevel.Neighborhood, Now.AddHours(-25));

    private static Agency BuildAgency(Guid id, Guid ownerUserId)
    {
        // Same reflection workaround OfficeMatchingServiceTests.BuildAgency already uses:
        // Agency.Create has no way to assign a caller-chosen id.
        var agency = Agency.Create("Test Agency", $"test-{id:N}", ownerUserId, "SY", Now);
        typeof(Agency).GetProperty(nameof(Agency.Id))!.SetValue(agency, id);
        return agency;
    }
}
