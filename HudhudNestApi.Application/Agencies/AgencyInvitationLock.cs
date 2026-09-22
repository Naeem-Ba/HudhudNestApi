namespace HudhudNestApi.Application.Agencies;

/// <summary>
/// Derives PostgreSQL advisory-lock keys for AgencyInvitation's two race windows
/// (B-2, RELEASE-BLOCKERS-AR.md) — the same tool ListingQuotaLock uses for B-10's
/// count-then-insert race, applied here to two different windows:
///
///  - ForPair: two concurrent invites for the same agency+user (Scenario A). The unique
///    filtered index on (AgencyId, TargetUserId) WHERE Pending is the final backstop, but
///    the lock is what lets CreateAgencyInvitationCommandHandler check-then-act (lazily
///    expire a stale Pending row, then insert a fresh one) without a second racing request
///    doing the same thing and hitting the index instead of a clean ConflictException.
///
///  - ForTargetUser: everything that can end an invitation for one user — accepting it,
///    declining it, or accepting a *different* agency's invitation (JoinAgency only allows
///    one) — serializes on the same key. Two Accept calls, or an Accept racing a Decline
///    (Scenarios B/C), can no longer both win: the second one re-reads the row after
///    acquiring the lock and finds it already terminal.
///
/// Same 128→64 bit XOR-with-a-tag construction as ListingQuotaLock, for the same reason:
/// a truncation collision between an agency id and a user id must not let one lock stand
/// in for the other.
/// </summary>
public static class AgencyInvitationLock
{
    private const long PairDomainTag = 0x4149_5041_4952_3031; // "AIPAIR01" (ASCII bytes)
    private const long TargetDomainTag = 0x4149_5452_4754_3031; // "AITRGT01" (ASCII bytes)

    public static long ForPair(Guid agencyId, Guid targetUserId)
        => Fold(agencyId) ^ Fold(targetUserId) ^ PairDomainTag;

    public static long ForTargetUser(Guid targetUserId)
        => Fold(targetUserId) ^ TargetDomainTag;

    private static long Fold(Guid id) => BitConverter.ToInt64(id.ToByteArray(), 0);
}
