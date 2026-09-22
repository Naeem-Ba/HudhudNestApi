using HudhudNestApi.Domain.Agencies.Enums;
using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Domain.Agencies.Entities;

/// <summary>
/// An agency owner's request that a specific user join their agency — the consent step
/// B-2 (RELEASE-BLOCKERS-AR.md) found missing. Nothing here grants membership by itself:
/// UserAccount.AgencyId is only ever set by <see cref="Accept"/>, never by <see cref="Create"/>.
///
/// DDD: private setters + factory method + domain state-machine methods, same shape as
/// VisitRequest (Bookings) — Pending is the only state anything transitions OUT of;
/// Accepted/Declined/Expired are all terminal.
///
/// Expiration is computed (<see cref="IsExpired"/>), not swept by a background job — the
/// same choice PhoneOtpChallenge makes. <see cref="MarkExpiredIfDue"/> persists that computed fact
/// the next time the row is touched (an accept attempt, or a new invite for the same
/// agency+user), which is also what lets an owner re-invite once a stale invitation's
/// window has passed without needing a cleanup job to run first.
/// </summary>
public sealed class AgencyInvitation : BaseEntity
{
    /// <summary>How long an invitation stays actionable after creation.</summary>
    public static readonly TimeSpan DefaultExpiryWindow = TimeSpan.FromDays(14);

    private AgencyInvitation() { }

    public Guid AgencyId { get; private set; }

    /// <summary>The agency owner who sent the invitation.</summary>
    public Guid InviterUserId { get; private set; }

    /// <summary>The user being invited — the only user who may accept or decline this.</summary>
    public Guid TargetUserId { get; private set; }

    public AgencyInvitationStatus Status { get; private set; } = AgencyInvitationStatus.Pending;

    public DateTime ExpiresAt { get; private set; }

    /// <summary>When the target user accepted or declined. Null while Pending.</summary>
    public DateTime? RespondedAt { get; private set; }

    public static AgencyInvitation Create(
        Guid agencyId,
        Guid inviterUserId,
        Guid targetUserId,
        DateTime utcNow,
        TimeSpan? expiryWindow = null)
    {
        if (agencyId == Guid.Empty)
            throw new DomainException("Agency id is required.");

        if (inviterUserId == Guid.Empty)
            throw new DomainException("Inviter id is required.");

        if (targetUserId == Guid.Empty)
            throw new DomainException("Target user id is required.");

        // Defense in depth — the application layer also rejects this before it ever
        // reaches here, but a domain invariant should not depend on its only caller
        // remembering to check.
        if (targetUserId == inviterUserId)
            throw new DomainException("لا يمكن دعوة نفسك للانضمام إلى مكتبك.");

        return new AgencyInvitation
        {
            AgencyId = agencyId,
            InviterUserId = inviterUserId,
            TargetUserId = targetUserId,
            Status = AgencyInvitationStatus.Pending,
            ExpiresAt = utcNow.Add(expiryWindow ?? DefaultExpiryWindow),
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    /// <summary>True once the actionable window has passed, regardless of what Status still says.</summary>
    public bool IsExpired(DateTime utcNow)
        => Status == AgencyInvitationStatus.Pending && utcNow >= ExpiresAt;

    /// <summary>
    /// Persists the Expired status if the invitation is Pending but past its window.
    /// Returns true when it flipped the row, so callers can react (e.g. stop treating it
    /// as a live duplicate). A no-op for any invitation that is not both Pending and due.
    /// </summary>
    public bool MarkExpiredIfDue(DateTime utcNow)
    {
        if (!IsExpired(utcNow))
            return false;

        Status = AgencyInvitationStatus.Expired;
        UpdatedAt = utcNow;
        return true;
    }

    public void Accept(DateTime utcNow)
    {
        if (IsExpired(utcNow))
        {
            MarkExpiredIfDue(utcNow);
            throw new DomainException("انتهت صلاحية هذه الدعوة.");
        }

        EnsureStatus(AgencyInvitationStatus.Pending, "قبول");

        Status = AgencyInvitationStatus.Accepted;
        RespondedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void Decline(DateTime utcNow)
    {
        EnsureStatus(AgencyInvitationStatus.Pending, "رفض");

        Status = AgencyInvitationStatus.Declined;
        RespondedAt = utcNow;
        UpdatedAt = utcNow;
    }

    private void EnsureStatus(AgencyInvitationStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainException($"لا يمكن {action} دعوة في الحالة '{Status}'.");
    }
}
