using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Domain.Users.Entities;

/// <summary>
/// A provable record that a user explicitly agreed to one version of one policy document
/// (Privacy Policy or Terms of Service), from one client surface, at one point in time.
///
/// Added for docs/privacy/privacy-gaps.md (P1): before this, the app recorded no consent
/// event at all — registration collected no acknowledgement of either document, so there
/// was nothing to show a user (or a regulator) as proof they had agreed, and no way to
/// detect who still needs to re-consent after a policy update.
///
/// Deliberately NOT a soft-deletable BaseEntity: a consent record must stay fully visible
/// and queryable forever, including after withdrawal — <see cref="Withdraw"/> sets
/// <see cref="WithdrawnAtUtc"/> rather than hiding or removing the row, because "the user
/// consented on this date, then withdrew on that date" is itself the auditable fact this
/// entity exists to preserve.
/// </summary>
public sealed class ConsentRecord
{
    private ConsentRecord() { }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public ConsentPolicyType PolicyType { get; private set; }

    /// <summary>
    /// The policy's version identifier at the moment of consent (e.g. "2026-09-04" or
    /// "1.0") — not the CURRENT version. If the policy is updated later, this value must
    /// stay exactly what it was when the user agreed, so re-consent can be required
    /// precisely for the users who agreed to an older version.
    /// </summary>
    public string PolicyVersion { get; private set; } = string.Empty;

    public DateTime ConsentedAtUtc { get; private set; }

    public ConsentSource Source { get; private set; }

    public DateTime? WithdrawnAtUtc { get; private set; }

    public bool IsActive => WithdrawnAtUtc is null;

    public static ConsentRecord Create(
        Guid userId,
        ConsentPolicyType policyType,
        string policyVersion,
        ConsentSource source,
        DateTime consentedAtUtc)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");

        if (string.IsNullOrWhiteSpace(policyVersion))
            throw new DomainException("PolicyVersion is required.");

        return new ConsentRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PolicyType = policyType,
            PolicyVersion = policyVersion.Trim(),
            Source = source,
            ConsentedAtUtc = consentedAtUtc
        };
    }

    /// <summary>
    /// Records that the user withdrew this consent. Idempotent — withdrawing an
    /// already-withdrawn record keeps the original withdrawal timestamp rather than
    /// overwriting it, so the audit trail never silently moves.
    /// </summary>
    public void Withdraw(DateTime withdrawnAtUtc)
    {
        if (WithdrawnAtUtc is not null)
            return;

        WithdrawnAtUtc = withdrawnAtUtc;
    }
}
