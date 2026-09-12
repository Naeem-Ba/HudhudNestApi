using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;
using PropertyApi.Infrastructure.Valuation;

namespace PropertyApi.Architecture.Tests.Persistence;

/// <summary>
/// Valuation Stage 5 (24h SLA) — the actual DB-side "never left indefinitely pending" rules
/// (ValuationInquiryRepository.ApplyDueForExpiryFilter,
/// ValuationOfficeInvitationRepository.ApplyStaleSentFilter). Lives here rather than in
/// PropertyApi.Application.Tests for the same reason AgencyLocationMatchingTests (Stage 4) /
/// ComparableListingsMatchingTests (Stage 3) do: PropertyApi.Application.Tests deliberately
/// does not reference PropertyApi.Infrastructure. Exercised over LINQ-to-Objects, no database
/// involved — ValuationSlaEnforcementServiceTests (Application.Tests) separately covers the
/// service's own decision logic against Moq'd repositories.
/// </summary>
public sealed class ValuationSlaFilterTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── ValuationInquiryRepository.ApplyDueForExpiryFilter ──────────

    [Theory]
    [InlineData(ValuationInquiryStatus.Pending)]
    [InlineData(ValuationInquiryStatus.MatchedFromListings)]
    [InlineData(ValuationInquiryStatus.AwaitingOfficeResponses)]
    public void DueForExpiry_IncludesEveryNonTerminalStatus_OnceExpiresAtHasPassed(ValuationInquiryStatus status)
    {
        var inquiry = BuildInquiry(status, expiresAt: Now.AddMinutes(-1));

        var results = FilterDueInquiries([inquiry]);

        Assert.Equal([inquiry], results);
    }

    [Theory]
    [InlineData(ValuationInquiryStatus.Completed)]
    [InlineData(ValuationInquiryStatus.Expired)]
    public void DueForExpiry_ExcludesTerminalStatus_RegardlessOfExpiresAt(ValuationInquiryStatus status)
    {
        // Idempotency: an already-terminal row must never be returned again, even though its
        // ExpiresAt is (necessarily, for Expired) or could be (for Completed) in the past.
        var inquiry = BuildInquiry(status, expiresAt: Now.AddDays(-1));

        var results = FilterDueInquiries([inquiry]);

        Assert.Empty(results);
    }

    [Fact]
    public void DueForExpiry_ExcludesNonTerminalInquiry_WhoseWindowHasNotClosedYet()
    {
        var inquiry = BuildInquiry(ValuationInquiryStatus.Pending, expiresAt: Now.AddHours(1));

        var results = FilterDueInquiries([inquiry]);

        Assert.Empty(results);
    }

    [Fact]
    public void DueForExpiry_ExpiresAtExactlyNow_IsIncluded()
    {
        // >= , not > : "24h have elapsed" includes the exact boundary instant, same convention
        // ValuationInquiry.IsExpired/ValuationOfficeInvitation.IsExpired already use.
        var inquiry = BuildInquiry(ValuationInquiryStatus.Pending, expiresAt: Now);

        var results = FilterDueInquiries([inquiry]);

        Assert.Equal([inquiry], results);
    }

    // ── ValuationOfficeInvitationRepository.ApplyStaleSentFilter ────

    [Fact]
    public void StaleSent_IncludesSentInvitation_WhoseParentInquiryExpiresAtHasPassed()
    {
        var inquiry = BuildInquiry(ValuationInquiryStatus.AwaitingOfficeResponses, expiresAt: Now.AddMinutes(-1));
        var invitation = BuildInvitation(inquiry.Id, ValuationOfficeInvitationStatus.Sent);

        var results = FilterStaleInvitations([invitation], [inquiry]);

        Assert.Equal([invitation], results);
    }

    [Theory]
    [InlineData(ValuationInquiryStatus.Completed)]
    [InlineData(ValuationInquiryStatus.Expired)]
    public void StaleSent_IncludesSentInvitation_WhoseParentInquiryIsAlreadyTerminal_EvenBeforeItsOwnExpiresAt(
        ValuationInquiryStatus parentStatus)
    {
        // The dangling-invitation safety net: an inquiry can finish (Completed) or be expired
        // by the other phase before this specific invitation's own cutoff arrives -- e.g.
        // enough OTHER invited offices already responded. Without this, the invitation would
        // sit "Sent" forever with no office ever needing to act on it and no one ever telling
        // them so -- exactly the "expired invitations can still be responded to" failure mode
        // this module's rules forbid.
        var inquiry = BuildInquiry(parentStatus, expiresAt: Now.AddDays(1)); // window still "open" by the clock alone
        var invitation = BuildInvitation(inquiry.Id, ValuationOfficeInvitationStatus.Sent);

        var results = FilterStaleInvitations([invitation], [inquiry]);

        Assert.Equal([invitation], results);
    }

    [Fact]
    public void StaleSent_ExcludesSentInvitation_WhoseParentInquiryIsStillActive_AndNotYetDue()
    {
        var inquiry = BuildInquiry(ValuationInquiryStatus.AwaitingOfficeResponses, expiresAt: Now.AddHours(1));
        var invitation = BuildInvitation(inquiry.Id, ValuationOfficeInvitationStatus.Sent);

        var results = FilterStaleInvitations([invitation], [inquiry]);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(ValuationOfficeInvitationStatus.Responded)]
    [InlineData(ValuationOfficeInvitationStatus.Expired)]
    public void StaleSent_ExcludesInvitation_NotInSentStatus_RegardlessOfParent(
        ValuationOfficeInvitationStatus invitationStatus)
    {
        // Idempotency: an invitation already Responded or already Expired must never be
        // returned/re-expired again, even though its parent inquiry has long since expired.
        var inquiry = BuildInquiry(ValuationInquiryStatus.Expired, expiresAt: Now.AddDays(-1));
        var invitation = BuildInvitation(inquiry.Id, invitationStatus);

        var results = FilterStaleInvitations([invitation], [inquiry]);

        Assert.Empty(results);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static List<ValuationInquiry> FilterDueInquiries(IEnumerable<ValuationInquiry> inquiries)
        => ValuationInquiryRepository.ApplyDueForExpiryFilter(inquiries.AsQueryable(), Now).ToList();

    private static List<ValuationOfficeInvitation> FilterStaleInvitations(
        IEnumerable<ValuationOfficeInvitation> invitations,
        IEnumerable<ValuationInquiry> inquiries)
        => ValuationOfficeInvitationRepository
            .ApplyStaleSentFilter(invitations.AsQueryable(), inquiries.AsQueryable(), Now)
            .ToList();

    private static ValuationInquiry BuildInquiry(ValuationInquiryStatus status, DateTime expiresAt)
    {
        // ExpiresAt is always CreatedAt + 24h (Create's own invariant), so it is set here by
        // choosing CreatedAt = expiresAt - 24h, then driven to the target Status via the
        // entity's own state-machine methods rather than reflection -- every status here is
        // reachable through a valid transition sequence from Pending.
        var createdAt = expiresAt.Subtract(ValuationInquiry.DefaultExpiryWindow);
        var inquiry = ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: createdAt);

        switch (status)
        {
            case ValuationInquiryStatus.Pending:
                break;
            case ValuationInquiryStatus.MatchedFromListings:
                inquiry.MarkMatchedFromListings(createdAt);
                break;
            case ValuationInquiryStatus.AwaitingOfficeResponses:
                inquiry.MarkAwaitingOfficeResponses(createdAt);
                break;
            case ValuationInquiryStatus.Completed:
                inquiry.MarkMatchedFromListings(createdAt);
                inquiry.MarkCompleted(createdAt);
                break;
            case ValuationInquiryStatus.Expired:
                inquiry.Expire(createdAt);
                break;
        }

        return inquiry;
    }

    private static ValuationOfficeInvitation BuildInvitation(Guid inquiryId, ValuationOfficeInvitationStatus status)
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), inquiryId, ValuationMatchLevel.Neighborhood, Now.AddHours(-25));

        switch (status)
        {
            case ValuationOfficeInvitationStatus.Sent:
                break;
            case ValuationOfficeInvitationStatus.Responded:
                invitation.MarkResponded(Now);
                break;
            case ValuationOfficeInvitationStatus.Expired:
                invitation.Expire(Now);
                break;
        }

        return invitation;
    }
}
