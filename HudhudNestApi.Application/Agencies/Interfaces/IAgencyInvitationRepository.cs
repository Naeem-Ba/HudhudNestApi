using HudhudNestApi.Domain.Agencies.Entities;

namespace HudhudNestApi.Application.Agencies.Interfaces;

/// <summary>
/// Persistence seam for AgencyInvitation — separate from IAgencyRepository because an
/// invitation is its own aggregate with its own lifecycle, the same split IVisitRepository
/// draws from the Property repository for VisitRequest.
/// </summary>
public interface IAgencyInvitationRepository
{
    Task AddAsync(AgencyInvitation invitation, CancellationToken ct = default);

    /// <summary>Tracked — callers mutate it through Accept/Decline/MarkExpiredIfDue.</summary>
    Task<AgencyInvitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default);

    /// <summary>
    /// The Pending invitation for this agency+user pair, if one exists — regardless of
    /// whether its window has already passed (callers decide what a stale-but-still-Pending
    /// row means; CreateAgencyInvitationCommandHandler lazily expires it before re-inviting).
    /// Tracked, for the same reason as GetByIdAsync.
    /// </summary>
    Task<AgencyInvitation?> GetPendingAsync(
        Guid agencyId,
        Guid targetUserId,
        CancellationToken ct = default);

    /// <summary>
    /// The target user's actionable invitations — Pending and not yet past ExpiresAt.
    /// Read-only; this never flips a stale row to Expired (no handler here mutates).
    /// </summary>
    Task<IReadOnlyList<AgencyInvitation>> GetActionableForTargetAsync(
        Guid targetUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Re-reads this tracked invitation's current column values from the database, in
    /// place. Used after acquiring an AgencyInvitationLock advisory lock: the entity may
    /// have been loaded (and its Status cached in memory) before the lock was acquired, and
    /// a concurrent request could have committed a transition while this one was waiting —
    /// this is what makes the post-lock state check see that transition instead of stale
    /// data from the pre-lock read.
    /// </summary>
    Task ReloadAsync(AgencyInvitation invitation, CancellationToken ct = default);
}
