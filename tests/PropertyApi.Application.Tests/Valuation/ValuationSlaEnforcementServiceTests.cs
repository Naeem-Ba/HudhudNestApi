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
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        notifications
            .Setup(x => x.NotifyValuationInquiryExpiredAsync(requesterId, inquiry.Id, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RunSweepAsync(Now, batchSize: 200);

        Assert.Equal(1, result.InquiriesExpired);
        Assert.Equal(0, result.InvitationsExpired);
        Assert.Equal(ValuationInquiryStatus.Expired, inquiry.Status);
        inquiries.Verify(x => x.Update(inquiry), Times.Once);
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
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
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
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        // Strict mock on unitOfWork with no SaveChangesAsync setup means this would already
        // throw if called — the assertion below is redundant confirmation of that.
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
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
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
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
